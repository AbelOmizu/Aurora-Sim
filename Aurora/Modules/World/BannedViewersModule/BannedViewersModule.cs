using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Xml;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Framework.Interfaces;
using Aurora.Framework;
using Nini.Config;

namespace Aurora.Modules
{
    public class BannedViewersModule: ISharedRegionModule, IBanViewersModule
    {
        private List<string> m_bannedViewers = new List<string>();
        private bool m_banEvilViewersByDefault = true;
        private bool m_enabled = true;
        private OSDMap m_map = null;

        public List<string> BannedViewers
        {
            get
            {
                return m_bannedViewers;
            }
        }

        public void Initialise(IConfigSource source)
        {
            IConfig config = source.Configs["BanViewersModule"];
            if (config != null)
            {
                string bannedViewers = config.GetString("ViewersToBan", "");
                m_banEvilViewersByDefault = config.GetBoolean("BanKnownEvilViewers", true);
                m_bannedViewers = new List<string>(bannedViewers.Split(','));
                m_enabled = config.GetBoolean("Enabled", true);
            }
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        public void AddRegion(Scene scene)
        {
            if(m_enabled)
                scene.RegisterModuleInterface<IBanViewersModule>(this);
        }

        public void RemoveRegion(Scene scene)
        {
        }

        public void RegionLoaded(Scene scene)
        {
        }

        public string Name
        {
            get { return "BanViewersModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        /// <summary>
        /// Check to see if the client has baked textures that belong to banned clients
        /// </summary>
        /// <param name="client"></param>
        /// <param name="textureEntry"></param>
        public void CheckForBannedViewer(IClientAPI client, Primitive.TextureEntry textureEntry)
        {
            try
            {
                //Read the website once!
                if (m_map == null)
                    m_map = (OSDMap)OSDParser.Deserialize(Utilities.ReadExternalWebsite("http://auroraserver.ath.cx:8080/client_tags.xml"));
                
                //This is the givaway texture!
                for (int i = 0; i < textureEntry.FaceTextures.Length; i++)
                {
                    if (textureEntry.FaceTextures[i] != null)
                    {
                        if (m_map.ContainsKey(textureEntry.FaceTextures[i].TextureID.ToString()))
                        {
                            OSDMap viewerMap = (OSDMap)m_map[textureEntry.FaceTextures[i].TextureID.ToString()];
                            //Check the names
                            if (BannedViewers.Contains(viewerMap["name"].ToString()))
                            {
                                client.Kick("You cannot use " + viewerMap["name"] + " in this sim.");
                                IEntityTransferModule transferModule = client.Scene.RequestModuleInterface<IEntityTransferModule> ();
                                if (transferModule != null)
                                    transferModule.IncomingCloseAgent (((Scene)client.Scene), client.AgentId);
                            }
                            else if (m_banEvilViewersByDefault && viewerMap.ContainsKey("evil") && (viewerMap["evil"].AsBoolean() == true))
                            {
                                client.Kick("You cannot use " + viewerMap["name"] + " in this sim.");
                                IEntityTransferModule transferModule = client.Scene.RequestModuleInterface<IEntityTransferModule> ();
                                if (transferModule != null)
                                    transferModule.IncomingCloseAgent (((Scene)client.Scene), client.AgentId);
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }
}
