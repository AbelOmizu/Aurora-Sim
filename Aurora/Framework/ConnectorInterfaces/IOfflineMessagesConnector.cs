using System;
using System.Collections.Generic;
using System.Text;
using Aurora.Framework;
using OpenMetaverse;
using OpenSim.Framework;

namespace Aurora.Framework
{
    public interface IOfflineMessagesConnector : IAuroraDataPlugin
	{
        /// <summary>
        /// Gets all offline messages for the user in GridInstantMessage format.
        /// </summary>
        /// <param name="agentID"></param>
        /// <returns></returns>
        GridInstantMessage[] GetOfflineMessages(UUID agentID);

        /// <summary>
        /// Adds a new offline message for the user.
        /// </summary>
        /// <param name="message"></param>
        /// <returns>Whether it was successfully added</returns>
        bool AddOfflineMessage(GridInstantMessage message);
	}
}
