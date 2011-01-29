﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;
using Aurora.Simulation.Base;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using log4net;

namespace OpenSim.Services.Connectors.ConfigurationService
{
    public class ConfigurationService : IConfigurationService, IApplicationPlugin
    {
        protected static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);
        protected IConfigSource m_config;
        protected OSDMap m_autoConfig = new OSDMap();

        public virtual string Name
        {
            get { return GetType().Name; }
        }

        public virtual void Initialize(ISimulationBase openSim)
        {
            string resp = "";
            m_config = openSim.ConfigSource;

            IConfig handlerConfig = m_config.Configs["Handlers"];
            if (handlerConfig.GetString("ConfigurationHandler", "") != Name)
                return;

            //Register by default as this only gets used in remote grid mode
            openSim.ApplicationRegistry.RegisterModuleInterface<IConfigurationService>(this);

            IConfig autoConfig = m_config.Configs["Configuration"];
            if (autoConfig == null)
                return;

            while (resp == "")
            {
                string serverURL = autoConfig.GetString("ConfigurationURL", "");
                //Clean up the URL so that it isn't too hard for users
                serverURL = serverURL.EndsWith("/") ? serverURL.Remove(serverURL.Length - 1) : serverURL;
                serverURL += "/autoconfig";
                resp = SynchronousRestFormsRequester.MakeRequest("POST", serverURL, "");

                if (resp == "")
                {
                    m_log.ErrorFormat("[Configuration]: Failed to find the configuration for {0}!"
                        + " This may break this startup!", serverURL);
                    MainConsole.Instance.CmdPrompt("Press enter when you are ready to continue.");
                }
            }

            m_autoConfig = (OSDMap)OSDParser.DeserializeJson(resp);
        }

        public void ReloadConfiguration(IConfigSource config)
        {
        }

        public void PostInitialise()
        {
        }

        public void Start()
        {
        }

        public void PostStart()
        {
        }

        public void Close()
        {
        }

        public void Dispose()
        {
        }

        public virtual void AddNewUser(UUID userID, OSDMap urls)
        {
        }

        public virtual OSDMap GetDefaultValues()
        {
            return m_autoConfig;
        }

        public virtual List<string> FindValueOf(string key)
        {
            List<string> keys = new List<string>();

            if (m_autoConfig.ContainsKey(key))
            {
                keys = FindValueOfFromOSDMap(key, m_autoConfig);
            }
            else
            {
                keys = FindValueOfFromConfiguration(key);
            }
            return keys;
        }

        public virtual List<string> FindValueOf(UUID userID, string key)
        {
            return FindValueOf(key);
        }

        public virtual List<string> FindValueOfFromOSDMap(string key, OSDMap urls)
        {
            List<string> keys = new List<string>();

            string[] configKeys = urls[key].AsString().Split(',');
            keys.AddRange(configKeys);

            return keys;
        }

        public virtual List<string> FindValueOfFromConfiguration(string key)
        {
            List<string> keys = new List<string>();

            //We can safely assume that because we are registered, this will not be null
            string[] configKeys = m_config.Configs["Configuration"].GetString(key, "").Split(',');
            keys.AddRange(configKeys);

            return keys;
        }
    }
}
