using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Aurora.Framework;
using Aurora.DataManager;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using log4net;
using System.IO;
using System.Reflection;
using Nini.Config;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;
using Aurora.Simulation.Base;

namespace Aurora.Services.DataService
{
    public class RemoteAgentConnector : IAgentConnector
    {
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        private ExpiringCache<UUID, IAgentInfo> m_cache = new ExpiringCache<UUID, IAgentInfo>();
        private List<string> m_ServerURIs = new List<string>();

        public void Initialize(IGenericData unneeded, ISimulationBase simBase, string defaultConnectionString)
        {
            IConfigSource source = simBase.ConfigSource;
            if (source.Configs["AuroraConnectors"].GetString("AgentConnector", "LocalConnector") == "RemoteConnector")
            {
                m_ServerURIs = simBase.ApplicationRegistry.RequestModuleInterface<IConfigurationService>().FindValueOf("RemoteServerURI");
                if (m_ServerURIs.Count != 0)
                    DataManager.DataManager.RegisterPlugin(Name, this);
            }
        }

        public string Name
        {
            get { return "IAgentConnector"; }
        }

        public void Dispose()
        {
        }

        #region IAgentConnector Members

        public IAgentInfo GetAgent(UUID PrincipalID)
        {
            IAgentInfo agent;
            if (!m_cache.TryGetValue(PrincipalID, out agent))
                return agent;
                        
            Dictionary<string, object> sendData = new Dictionary<string, object>();

            sendData["PRINCIPALID"] = PrincipalID.ToString();
            sendData["METHOD"] = "getagent";

            string reqString = WebUtils.BuildQueryString(sendData);

            try
            {
                foreach (string m_ServerURI in m_ServerURIs)
                {
                    string reply = SynchronousRestFormsRequester.MakeRequest("POST",
                            m_ServerURI + "/auroradata",
                            reqString);
                    if (reply != string.Empty)
                    {
                        Dictionary<string, object> replyData = WebUtils.ParseXmlResponse(reply);

                        if (replyData != null)
                        {
                            if (!replyData.ContainsKey("result"))
                                return null;

                            Dictionary<string, object>.ValueCollection replyvalues = replyData.Values;
                            foreach (object f in replyvalues)
                            {
                                if (f is Dictionary<string, object>)
                                {
                                    agent = new IAgentInfo();
                                    agent.FromKVP((Dictionary<string, object>)f);
                                    m_cache.AddOrUpdate(PrincipalID, agent, new TimeSpan(0, 30, 0));
                                }
                                else
                                    m_log.DebugFormat("[AuroraRemoteAgentConnector]: GetAgent {0} received invalid response type {1}",
                                        PrincipalID, f.GetType());
                            }
                            // Success
                            return agent;
                        }

                        else
                            m_log.DebugFormat("[AuroraRemoteAgentConnector]: GetAgent {0} received null response",
                                PrincipalID);
                    }
                }
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[AuroraRemoteAgentConnector]: Exception when contacting server: {0}", e.ToString());
            }

            return null;
        }

        public void UpdateAgent(IAgentInfo agent)
        {
            //No creating from sims!
        }

        public void CreateNewAgent(UUID PrincipalID)
        {
            //No creating from sims!
        }

        public bool CheckMacAndViewer(string Mac, string viewer, out string reason)
        {
            //Only local! You should not be calling this!! This method is only called 
            // from LLLoginHandlers.
            reason = "";
            return false;
        }

        #endregion
    }
}
