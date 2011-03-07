using System;
using System.Collections.Generic;
using System.Text;
using OpenMetaverse;
using Aurora.Framework;
using OpenSim.Framework;

namespace Aurora.Framework
{
    public interface IRegionConnector : IAuroraDataPlugin
	{
        /// <summary>
        /// Adds a telehub for the region
        /// </summary>
        /// <param name="telehub"></param>
        void AddTelehub(Telehub telehub, ulong regionHandle);

        /// <summary>
        /// Removes the telehub for the region
        /// </summary>
        /// <param name="regionID"></param>
        void RemoveTelehub(UUID regionID, ulong regionHandle);

        /// <summary>
        /// Finds the telehub for the region
        /// </summary>
        /// <param name="regionID"></param>
        /// <returns></returns>
        Telehub FindTelehub(UUID regionID, ulong regionHandle);
    }
}
