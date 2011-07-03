/*
 * Copyright (c) Contributors, http://aurora-sim.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the Aurora-Sim Project nor the
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
using System.Reflection;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using Aurora.Framework;

namespace Aurora.Modules
{
    public class AuroraOfflineMessageModule : ISharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private bool enabled = true;
        private List<Scene> m_SceneList = new List<Scene>();
        IMessageTransferModule m_TransferModule = null;
        private bool m_ForwardOfflineGroupMessages = true;
        private IOfflineMessagesConnector OfflineMessagesConnector;
        private bool m_SendOfflineMessagesToEmail = false;

        public void Initialise(IConfigSource config)
        {
            IConfig cnf = config.Configs["Messaging"];
            if (cnf == null)
            {
                enabled = false;
                return;
            }
            if (cnf != null && cnf.GetString("OfflineMessageModule", "AuroraOfflineMessageModule") !=
                    "AuroraOfflineMessageModule")
            {
                enabled = false;
                return;
            }

            m_ForwardOfflineGroupMessages = cnf.GetBoolean ("ForwardOfflineGroupMessages", m_ForwardOfflineGroupMessages);
            m_SendOfflineMessagesToEmail = cnf.GetBoolean ("SendOfflineMessagesToEmail", m_SendOfflineMessagesToEmail);
        }

        public void AddRegion(Scene scene)
        {
            if (!enabled)
                return;

            lock (m_SceneList)
            {
                m_SceneList.Add(scene);

                scene.EventManager.OnNewClient += OnNewClient;
                scene.EventManager.OnClosingClient += OnClosingClient;
            }
        }

        public void RegionLoaded(Scene scene)
        {
            if (!enabled)
                return;

            if (m_TransferModule == null)
            {
                OfflineMessagesConnector = Aurora.DataManager.DataManager.RequestPlugin<IOfflineMessagesConnector>();
                m_TransferModule = scene.RequestModuleInterface<IMessageTransferModule>();
                if (m_TransferModule == null)
                {
                    scene.EventManager.OnNewClient -= OnNewClient;
                    scene.EventManager.OnClosingClient -= OnClosingClient;

                    enabled = false;
                    m_SceneList.Clear();

                    m_log.Error("[OFFLINE MESSAGING] No message transfer module is enabled. Diabling offline messages");
                    return;
                }
                m_TransferModule.OnUndeliveredMessage += UndeliveredMessage;
            }
        }

        public void RemoveRegion(Scene scene)
        {
            if (!enabled)
                return;

            lock (m_SceneList)
            {
                m_SceneList.Remove(scene);
            }
            if (m_TransferModule != null)
            {
                scene.EventManager.OnNewClient -= OnNewClient;
                scene.EventManager.OnClosingClient -= OnClosingClient;
                m_TransferModule.OnUndeliveredMessage -= UndeliveredMessage;
            }
        }

        public void PostInitialise()
        {
            if (!enabled)
                return;

            //m_log.Debug("[OFFLINE MESSAGING] Offline messages enabled");
        }

        public string Name
        {
            get { return "AuroraOfflineMessageModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void Close()
        {
        }

        private Scene FindScene(UUID agentID)
        {
            foreach (Scene s in m_SceneList)
            {
                IScenePresence presence = s.GetScenePresence (agentID);
                if (presence != null && !presence.IsChildAgent)
                    return s;
            }
            return null;
        }

        private IClientAPI FindClient(UUID agentID)
        {
            foreach (Scene s in m_SceneList)
            {
                IScenePresence presence = s.GetScenePresence (agentID);
                if (presence != null && !presence.IsChildAgent)
                    return presence.ControllingClient;
            }
            return null;
        }

        private void OnNewClient(IClientAPI client)
        {
            client.OnRetrieveInstantMessages += RetrieveInstantMessages;
        }

        private void OnClosingClient(IClientAPI client)
        {
            client.OnRetrieveInstantMessages -= RetrieveInstantMessages;
        }

        private void RetrieveInstantMessages(IClientAPI client)
        {
            if (OfflineMessagesConnector == null)
                return;
            m_log.InfoFormat("[OFFLINE MESSAGING] Retrieving stored messages for {0}", client.AgentId);

            GridInstantMessage[] msglist = OfflineMessagesConnector.GetOfflineMessages(client.AgentId);

            foreach (GridInstantMessage IM in msglist)
            {
                // Send through scene event manager so all modules get a chance
                // to look at this message before it gets delivered.
                //
                // Needed for proper state management for stored group
                // invitations
                //
                IM.offline = 1;
                Scene s = FindScene(client.AgentId);
                if (s != null)
                    s.EventManager.TriggerIncomingInstantMessage(IM);
            }
        }

        private void UndeliveredMessage(GridInstantMessage im)
        {
            if (!OfflineMessagesConnector.AddOfflineMessage (im))
            {
                IClientAPI client = FindClient (im.fromAgentID);
                if (client != null)
                    client.SendInstantMessage (new GridInstantMessage (
                            null, im.toAgentID,
                            "System", im.fromAgentID,
                            (byte)InstantMessageDialog.MessageFromAgent,
                            "User has too many IMs already, please try again later.",
                            false, new Vector3 ()));
                return;
            }
            if ((im.offline != 0)
                && (!im.fromGroup || (im.fromGroup && m_ForwardOfflineGroupMessages)))
            {
                if (im.dialog == 32) //Group notice
                {
                    IGroupsModule module = m_SceneList[0].RequestModuleInterface<IGroupsModule>();
                    if (module != null)
                        im = module.BuildOfflineGroupNotice(im);
                }

                IEmailModule emailModule = m_SceneList[0].RequestModuleInterface<IEmailModule> ();
                if (emailModule != null && m_SendOfflineMessagesToEmail)
                {
                    IUserProfileInfo profile = Aurora.DataManager.DataManager.RequestPlugin<IProfileConnector> ().GetUserProfile (im.toAgentID);
                    if (profile.IMViaEmail)
                    {
                        UserAccount account = m_SceneList[0].UserAccountService.GetUserAccount (UUID.Zero, im.toAgentID.ToString ());
                        if (account != null && account.Email != "" && account.Email != null)
                        {
                            emailModule.SendEmail (UUID.Zero, account.Email, string.Format ("Offline Message from {0}", im.fromAgentName),
                                string.Format ("Time: {0}\n", Util.ToDateTime (im.timestamp).ToShortDateString ()) +
                                string.Format ("From: {0}\n", im.fromAgentName) +
                                string.Format ("Message: {0}\n", im.message));
                        }
                    }
                }

                if (im.dialog == (byte)InstantMessageDialog.MessageFromAgent)
                {
                    IClientAPI client = FindClient(im.fromAgentID);
                    if (client == null)
                        return;

                    client.SendInstantMessage(new GridInstantMessage(
                            null, im.toAgentID,
                            "System", im.fromAgentID,
                            (byte)InstantMessageDialog.MessageFromAgent,
                            "User is not logged in. Message saved.",
                            false, new Vector3()));
                }

                if (im.dialog == (byte)InstantMessageDialog.InventoryOffered)
                {
                    IClientAPI client = FindClient(im.fromAgentID);
                    if (client == null)
                        return;

                    client.SendAlertMessage("User is not online. Inventory has been saved");
                }
            }
            else if (im.offline == 0)
            {
                if (im.dialog == (byte)InstantMessageDialog.MessageFromAgent)
                {
                    IClientAPI client = FindClient(im.fromAgentID);
                    if (client == null)
                        return;

                    client.SendInstantMessage(new GridInstantMessage(
                            null, im.toAgentID,
                            "System", im.fromAgentID,
                            (byte)InstantMessageDialog.MessageFromAgent,
                            "User is not able to be found. Message saved.",
                            false, new Vector3()));
                }

                if (im.dialog == (byte)InstantMessageDialog.InventoryOffered)
                {
                    IClientAPI client = FindClient(im.fromAgentID);
                    if (client == null)
                        return;

                    client.SendAlertMessage("User not able to be found. Inventory has been saved");
                }
            }
        }
    }
}

