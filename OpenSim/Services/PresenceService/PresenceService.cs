/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using Nini.Config;
using log4net;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Data;
using OpenSim.Services.Interfaces;
using OpenMetaverse;
using Aurora.Framework;
using Aurora.Simulation.Base;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

namespace OpenSim.Services.PresenceService
{
    public class PresenceService : IPresenceService, IService
    {
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        private IGridService m_GridService;
        protected IPresenceData m_Database = null;

        protected bool m_allowDuplicatePresences = true;
        protected bool m_checkLastSeen = true;

        public void Initialize(IConfigSource config, IRegistryCore registry)
        {
            string dllName = String.Empty;
            string connString = String.Empty;
            ///This was decamel-cased, and it will break MONO appearently as MySQL on MONO cares about case.
            string realm = "Presence";

            //
            // Try reading the [DatabaseService] section, if it exists
            //
            IConfig dbConfig = config.Configs["DatabaseService"];
            if (dbConfig != null)
            {
                if (dllName == String.Empty)
                    dllName = dbConfig.GetString("StorageProvider", String.Empty);
                if (connString == String.Empty)
                    connString = dbConfig.GetString("ConnectionString", String.Empty);
            }

            //
            // [PresenceService] section overrides [DatabaseService], if it exists
            //
            IConfig presenceConfig = config.Configs["PresenceService"];
            if (presenceConfig != null)
            {
                dllName = presenceConfig.GetString("StorageProvider", dllName);
                connString = presenceConfig.GetString("ConnectionString", connString);
                realm = presenceConfig.GetString("Realm", realm);
            }

            //
            // We tried, but this doesn't exist. We can't proceed.
            //
            if (dllName.Equals(String.Empty))
                throw new Exception("No StorageProvider configured");

            m_Database = AuroraModuleLoader.LoadPlugin<IPresenceData>(dllName, new Object[] { connString, realm });
            if (m_Database == null)
                throw new Exception("Could not find a storage interface in the given module " + dllName);

            if (presenceConfig != null)
            {
                m_allowDuplicatePresences =
                       presenceConfig.GetBoolean("AllowDuplicatePresences",
                                                 m_allowDuplicatePresences);
                m_checkLastSeen =
                       presenceConfig.GetBoolean("CheckLastSeen",
                                                 m_checkLastSeen);
            }
            registry.RegisterModuleInterface<IPresenceService>(this);
            m_log.Debug("[PRESENCE SERVICE]: Starting presence service");
        }

        public void Start(IConfigSource config, IRegistryCore registry)
        {
            m_GridService = registry.RequestModuleInterface<IGridService>();
        }

        public bool LoginAgent(string userID, UUID sessionID,
                UUID secureSessionID)
        {
            if (!m_allowDuplicatePresences)
                m_Database.LogoutAgent(UUID.Parse(userID));

            PresenceData data = new PresenceData();

            data.UserID = userID;
            data.RegionID = UUID.Zero;
            data.SessionID = sessionID;
            data.Data = new Dictionary<string, string>();
            data.Data["SecureSessionID"] = secureSessionID.ToString();
            data.Data["LastSeen"] = Util.UnixTimeSinceEpoch().ToString();
            
            m_Database.Store(data);

            m_log.DebugFormat("[PRESENCE SERVICE]: LoginAgent {0} with session {1} and ssession {2}",
                userID, sessionID, secureSessionID);
            return true;
        }

        public bool LogoutAgent(UUID sessionID)
        {
            m_log.DebugFormat("[PRESENCE SERVICE]: Session {0} logout", sessionID);
            return m_Database.Delete("SessionID", sessionID.ToString());
        }

        public bool LogoutRegionAgents(UUID regionID)
        {
            m_Database.LogoutRegionAgents(regionID);

            return true;
        }

        public void ReportAgent(UUID sessionID, UUID regionID)
        {
            m_log.DebugFormat("[PRESENCE SERVICE]: ReportAgent with session {0} in region {1}", sessionID, regionID);
            try
            {
                PresenceData pdata = m_Database.Get(sessionID);
                if (pdata == null)
                    return;
                if (pdata.Data == null)
                    return;

                m_Database.ReportAgent(sessionID, regionID);
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[PRESENCE SERVICE]: ReportAgent threw exception {0}", e.StackTrace);
            }
        }

        public PresenceInfo GetAgent(UUID sessionID)
        {
            PresenceInfo ret = new PresenceInfo();
            
            PresenceData data = m_Database.Get(sessionID);
            if (data == null)
                return null;

            if (m_checkLastSeen && int.Parse(data.Data["LastSeen"]) + (1000 * 60 * 60) < Util.UnixTimeSinceEpoch())
            {
                m_log.Warn("[PresenceService]: Found a user (" + data.UserID + ") that was not seen within the last hour! Logging them out.");
                LogoutAgent(sessionID);
                return null;
            }

            ret.UserID = data.UserID;
            ret.RegionID = data.RegionID;

            return ret;
        }

        public PresenceInfo[] GetAgents(string[] userIDs)
        {
            List<PresenceInfo> info = new List<PresenceInfo>();

            foreach (string userIDStr in userIDs)
            {
                PresenceData[] data = m_Database.Get("UserID",
                        userIDStr);

                foreach (PresenceData d in data)
                {
                    PresenceInfo ret = new PresenceInfo();

                    if (m_checkLastSeen && int.Parse(d.Data["LastSeen"]) + (1000 * 60 * 60) < Util.UnixTimeSinceEpoch())
                    {
                        m_log.Warn("[PresenceService]: Found a user (" + d.UserID + ") that was not seen within the last hour! Logging them out.");
                        LogoutAgent(d.SessionID);
                        continue;
                    }

                    ret.UserID = d.UserID;
                    ret.RegionID = d.RegionID;

                    info.Add(ret);
                }
            }

            // m_log.DebugFormat("[PRESENCE SERVICE]: GetAgents for {0} userIDs found {1} presences", userIDs.Length, info.Count);
            return info.ToArray();
        }

        public string[] GetAgentsLocations(string[] userIDs)
        {
            List<string> info = new List<string>();

            foreach (string userIDStr in userIDs)
            {
                PresenceData[] data = m_Database.Get("UserID",
                        userIDStr);

                if (data.Length != 0)
                {
                    PresenceData d = data[0];
                    PresenceInfo ret = new PresenceInfo();

                    if (int.Parse(d.Data["LastSeen"]) + (1000 * 60 * 60) < Util.UnixTimeSinceEpoch())
                    {
                        m_log.Warn("[PresenceService]: Found a user (" + d.UserID + ") that was not seen within the last hour! Logging them out.");
                        LogoutAgent(d.SessionID);
                        info.Add("");
                        continue;
                    }
                    if (d.RegionID == UUID.Zero) //Bad logout
                    {
                        m_log.Warn("[PresenceService]: Found a user (" + d.UserID + ") that does not have a region (UUID.Zero)! Logging them out.");
                        LogoutAgent(d.SessionID);
                        info.Add("");
                        continue;
                    }

                    GridRegion r = m_GridService.GetRegionByUUID(UUID.Zero, d.RegionID);
                    if (r != null)
                        info.Add(r.ServerURI);
                }
                else//Add a blank one
                    info.Add("");
            }

            // m_log.DebugFormat("[PRESENCE SERVICE]: GetAgents for {0} userIDs found {1} presences", userIDs.Length, info.Count);
            return info.ToArray();
        }
    }
    public class AgentInfoService : IService, IAgentInfoService
    {
          #region Declares

         proteced IGenericsConnector m_genericsConnector;

          #endregion

         #region IService Members

         public void Initialize(IConfigSource config, IRegistryCore registry)
         {
         }
         public void Start(IConfigSource config, IRegistryCore registry)
         {
              m_genericsConnector = Aurora.DataManager.DataManager.RequestPlugin<IGenericsConnector>();
         }

         #endregion

         #region IAgentInfoService Members

         public UserInfo GetUserInfo(string userID)
         {
               return m_genericsConnector.GetGeneric<UserInfo>(UUID.Parse(userID), "UserInfo", userID, new UserInfo());
         }

         public bool SetHomePosition(string userID, UUID homeID, Vector3 homePosition, Vector3 homeLookAt)
         {
              UserInfo userInfo = GetUserInfo(userID);
              if(userInfo != null)
              {
                    userInfo.HomeRegionID = regionID;
                    userInfo.HomePosition = lastPosition;
                    userInfo.HomeLookAt = lastLookAt;
                    Save(userInfo[i]);
                    return true;
              }
              return false;
         }

         public void SetLastPosition(string userID, UUID regionID, Vector3 lastPosition, Vector3 lastLookAt)
         {
              UserInfo userInfo = GetUserInfo(userID);
              if(userInfo != null)
              {
                    userInfo.CurrentRegionID = regionID;
                    userInfo.CurrentPosition = lastPosition;
                    userInfo.CurrentLookAt = lastLookAt;
                    Save(userInfo);
              }
         }

        public void Save(UserInfo userInfo)
        {
               m_genericsConnector.AddGeneric(UUID.Parse(userInfo.UserID), "UserInfo", UserID, userInfo.ToOSD());
        }

         #endregion
    }
}
