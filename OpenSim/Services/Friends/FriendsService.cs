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

using OpenMetaverse;
using OpenSim.Framework;
using System;
using System.Collections.Generic;
using OpenSim.Services.Interfaces;
using OpenSim.Data;
using Nini.Config;
using log4net;
using FriendInfo = OpenSim.Services.Interfaces.FriendInfo;
using Aurora.Framework;
using Aurora.Simulation.Base;

namespace OpenSim.Services.Friends
{
    public class FriendsService : IFriendsService, IService
    {
        protected IFriendsData m_Database = null;

        public void Initialize(IConfigSource config, IRegistryCore registry)
        {
            string dllName = String.Empty;
            string connString = String.Empty;

            //
            // Try reading the [FriendsService] section first, if it exists
            //
            IConfig friendsConfig = config.Configs["FriendsService"];
            if (friendsConfig != null)
            {
                dllName = friendsConfig.GetString("StorageProvider", dllName);
                connString = friendsConfig.GetString("ConnectionString", connString);
            }

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
            // We tried, but this doesn't exist. We can't proceed.
            //
            if (String.Empty.Equals(dllName))
                throw new Exception("No StorageProvider configured");

            ///This was decamel-cased, and it will break MONO appearently as MySQL on MONO cares about case.
            string realm = "Friends";
            if (friendsConfig != null)
                realm = friendsConfig.GetString("Realm", realm);

            m_Database = AuroraModuleLoader.LoadPlugin<IFriendsData>(dllName, new Object[] { connString, realm });
            if (m_Database == null)
            {
                throw new Exception(
                    string.Format(
                        "Could not find a storage interface {0} in the given StorageProvider {1}", "IFriendsData", dllName));
            }
            registry.RegisterModuleInterface<IFriendsService>(this);
        }

        public void Start(IConfigSource config, IRegistryCore registry)
        {
        }

        public void AddNewRegistry(IConfigSource config, IRegistryCore registry)
        {
        }

        public FriendInfo[] GetFriends(UUID PrincipalID)
        {
            FriendsData[] data = m_Database.GetFriends(PrincipalID);

            List<FriendInfo> info = new List<FriendInfo>();

            foreach (FriendsData d in data)
            {
                FriendInfo i = new FriendInfo();

                i.PrincipalID = d.PrincipalID;
                i.Friend = d.Friend;
                i.MyFlags = Convert.ToInt32(d.Data["Flags"]);
                i.TheirFlags = Convert.ToInt32(d.Data["TheirFlags"]);

                info.Add(i);
            }

            return info.ToArray();
        }

        public bool StoreFriend(UUID PrincipalID, string Friend, int flags)
        {
            FriendsData d = new FriendsData();

            d.PrincipalID = PrincipalID;
            d.Friend = Friend;
            d.Data = new Dictionary<string, string>();
            d.Data["Flags"] = flags.ToString();

            return m_Database.Store(d);
        }

        public bool Delete(UUID PrincipalID, string Friend)
        {
            return m_Database.Delete(PrincipalID, Friend);
        }

    }
}
