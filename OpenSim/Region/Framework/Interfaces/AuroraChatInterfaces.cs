﻿using System;
using System.Collections.Generic;
using System.Reflection;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using log4net;
using Aurora.DataManager;
using Aurora.Framework;

namespace Aurora.Framework
{
    public interface IChatPlugin : IPlugin
    {
        void Initialize(IChatModule module);
        bool OnNewChatMessageFromWorld(OSChatMessage message, out OSChatMessage newmessage);

        void OnNewClient(IClientAPI client);

        void OnClosingClient(UUID clientID, Scene scene);
    }

    public interface IChatModule
    {
        void RegisterChatPlugin(string main, IChatPlugin plugin);
        int SayDistance { get; set; }
        int ShoutDistance { get; set; }
        int WhisperDistance { get; set; }
        IConfig Config { get; }
        void TrySendChatMessage (IScenePresence presence, Vector3 fromPos, Vector3 regionPos,
                                                  UUID fromAgentID, string fromName, ChatTypeEnum type,
                                                  string message, ChatSourceType src, float Range);
        void OnChatFromWorld(Object sender, OSChatMessage c);

        void DeliverChatToAvatars(ChatSourceType chatSourceType, OSChatMessage message);

        void SimChatBroadcast(string message, ChatTypeEnum type, int channel, Vector3 fromPos, string fromName,
                                     UUID fromID, bool fromAgent, UUID ToAgentID, Scene scene);
        void SimChat(string message, ChatTypeEnum type, int channel, Vector3 fromPos, string fromName,
                            UUID fromID, bool fromAgent, Scene scene);
        void SimChat(string message, ChatTypeEnum type, int channel, Vector3 fromPos, string fromName,
                               UUID fromID, bool fromAgent, bool broadcast, float range, UUID ToAgentID, Scene scene);
    }
}
