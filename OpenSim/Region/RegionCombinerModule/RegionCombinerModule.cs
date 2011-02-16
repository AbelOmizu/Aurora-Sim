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
using System.Reflection;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Client;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Framework.Console;
using OpenSim.Region.Physics.Manager;

namespace OpenSim.Region.RegionCombinerModule
{
    public class RegionCombinerModule : ISharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public string Name
        {
            get { return "RegionCombinerModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        private Dictionary<UUID, RegionConnections> m_regions = new Dictionary<UUID, RegionConnections>();
        private bool enabledYN = false;
        private Dictionary<UUID, Scene> m_startingScenes = new Dictionary<UUID, Scene>();
        private RegionCombinerLargeTerrainChannel landTerrainChannel = null;
        public void Initialise(IConfigSource source)
        {
            IConfig RegionCombinerConfig = source.Configs["RegionCombiner"];
            if(RegionCombinerConfig != null)
                enabledYN = RegionCombinerConfig.GetBoolean("CombineContiguousRegions", false);
            
            if (enabledYN)
                MainConsole.Instance.Commands.AddCommand("RegionCombinerModule", false, "fix-phantoms",
                    "Fix phantom objects", "Fixes phantom objects after an import to megaregions", FixPhantoms);
        }

        public void Close()
        {
        }

        public void AddRegion(Scene scene)
        {
        }

        public void RemoveRegion(Scene scene)
        {
            if (m_startingScenes.ContainsKey(scene.RegionInfo.RegionID))
                m_startingScenes.Remove(scene.RegionInfo.RegionID);
        }

        public void RegionLoaded(Scene scene)
        {
            if (enabledYN)
            {
                RegionLoadedDoWork(scene);

                scene.EventManager.OnNewPresence += NewPresence;
            }
        }

        private void NewPresence(ScenePresence presence)
        {
            if (presence.IsChildAgent)
            {
                byte[] throttleData;

                try
                {
                    throttleData = presence.ControllingClient.GetThrottlesPacked(1);
                } 
                catch (NotImplementedException)
                {
                    return;
                }

                if (throttleData == null)
                    return;

                if (throttleData.Length == 0)
                    return;

                if (throttleData.Length != 28)
                    return;

                byte[] adjData;
                int pos = 0;

                if (!BitConverter.IsLittleEndian)
                {
                    byte[] newData = new byte[7 * 4];
                    Buffer.BlockCopy(throttleData, 0, newData, 0, 7 * 4);

                    for (int i = 0; i < 7; i++)
                        Array.Reverse(newData, i * 4, 4);

                    adjData = newData;
                }
                else
                {
                    adjData = throttleData;
                }

                // 0.125f converts from bits to bytes
                int resend = (int)(BitConverter.ToSingle(adjData, pos) * 0.125f); pos += 4;
                int land = (int)(BitConverter.ToSingle(adjData, pos) * 0.125f); pos += 4;
                int wind = (int)(BitConverter.ToSingle(adjData, pos) * 0.125f); pos += 4;
                int cloud = (int)(BitConverter.ToSingle(adjData, pos) * 0.125f); pos += 4;
                int task = (int)(BitConverter.ToSingle(adjData, pos) * 0.125f); pos += 4;
                int texture = (int)(BitConverter.ToSingle(adjData, pos) * 0.125f); pos += 4;
                int asset = (int)(BitConverter.ToSingle(adjData, pos) * 0.125f);
                // State is a subcategory of task that we allocate a percentage to


                //int total = resend + land + wind + cloud + task + texture + asset;

                byte[] data = new byte[7 * 4];
                int ii = 0;

                Buffer.BlockCopy(Utils.FloatToBytes(resend), 0, data, ii, 4); ii += 4;
                Buffer.BlockCopy(Utils.FloatToBytes(land * 50), 0, data, ii, 4); ii += 4;
                Buffer.BlockCopy(Utils.FloatToBytes(wind), 0, data, ii, 4); ii += 4;
                Buffer.BlockCopy(Utils.FloatToBytes(cloud), 0, data, ii, 4); ii += 4;
                Buffer.BlockCopy(Utils.FloatToBytes(task), 0, data, ii, 4); ii += 4;
                Buffer.BlockCopy(Utils.FloatToBytes(texture), 0, data, ii, 4); ii += 4;
                Buffer.BlockCopy(Utils.FloatToBytes(asset), 0, data, ii, 4);

                try
                {
                    presence.ControllingClient.SetChildAgentThrottle(data);
                }
                catch (NotImplementedException)
                {
                    return;
                }
                presence.SetSendCourseLocationMethod(SendCourseLocationUpdates);
            }
        }

        private void RegionLoadedDoWork(Scene scene)
        {
/* 
            // For testing on a single instance
            if (scene.RegionInfo.RegionLocX == 1004 && scene.RegionInfo.RegionLocY == 1000)
                return;
            // 
*/
            if(landTerrainChannel == null)
                landTerrainChannel = new RegionCombinerLargeTerrainChannel();
            
            lock (m_startingScenes)
                m_startingScenes.Add(scene.RegionInfo.RegionID, scene);

            RegionConnections regionConnections = new RegionConnections();
            regionConnections.ConnectedRegions = new List<RegionData>();
            regionConnections.RegionScene = scene;
            regionConnections.RegionLandChannel = scene.RequestModuleInterface<IParcelManagementModule>();
            regionConnections.RegionId = scene.RegionInfo.RegionID;
            regionConnections.X = scene.RegionInfo.RegionLocX;
            regionConnections.Y = scene.RegionInfo.RegionLocY;
            regionConnections.XEnd = (int)Constants.RegionSize;
            regionConnections.YEnd = (int)Constants.RegionSize;


            lock (m_regions)
            {
                bool connectedYN = false;

                foreach (RegionConnections conn in m_regions.Values)
                {
                    #region commented
                    /*
                    // If we're one region over +x +y
                    //xxy
                    //xxx
                    //xxx
                    if ((((int)conn.X * (int)Constants.RegionSize) + conn.XEnd 
                        == (regionConnections.X * (int)Constants.RegionSize)) 
                        && (((int)conn.Y * (int)Constants.RegionSize) - conn.YEnd 
                        == (regionConnections.Y * (int)Constants.RegionSize)))
                    {
                        Vector3 offset = Vector3.Zero;
                        offset.X = (((regionConnections.X * (int) Constants.RegionSize)) -
                                    ((conn.X * (int) Constants.RegionSize)));
                        offset.Y = (((regionConnections.Y * (int) Constants.RegionSize)) -
                                    ((conn.Y * (int) Constants.RegionSize)));

                        Vector3 extents = Vector3.Zero;
                        extents.Y = regionConnections.YEnd + conn.YEnd;
                        extents.X = conn.XEnd + conn.XEnd;

                        m_log.DebugFormat("Scene: {0} to the northwest of Scene{1}.  Offset: {2}.  Extents:{3}",
                                          conn.RegionScene.RegionInfo.RegionName,
                                          regionConnections.RegionScene.RegionInfo.RegionName,
                                          offset, extents);

                        scene.PhysicsScene.Combine(conn.RegionScene.PhysicsScene, offset, extents);
                            
                        connectedYN = true;
                        break;
                    }
                    */

                    /*
                    //If we're one region over x +y
                    //xxx
                    //xxx
                    //xyx
                    if ((((int)conn.X * (int)Constants.RegionSize)
                        == (regionConnections.X * (int)Constants.RegionSize))
                        && (((int)conn.Y * (int)Constants.RegionSize) - conn.YEnd
                        == (regionConnections.Y * (int)Constants.RegionSize)))
                    {
                        Vector3 offset = Vector3.Zero;
                        offset.X = (((regionConnections.X * (int)Constants.RegionSize)) -
                                    ((conn.X * (int)Constants.RegionSize)));
                        offset.Y = (((regionConnections.Y * (int)Constants.RegionSize)) -
                                    ((conn.Y * (int)Constants.RegionSize)));

                        Vector3 extents = Vector3.Zero;
                        extents.Y = regionConnections.YEnd + conn.YEnd;
                        extents.X = conn.XEnd;

                        m_log.DebugFormat("Scene: {0} to the north of Scene{1}.  Offset: {2}. Extents:{3}",
                                          conn.RegionScene.RegionInfo.RegionName,
                                          regionConnections.RegionScene.RegionInfo.RegionName, offset, extents);

                        scene.PhysicsScene.Combine(conn.RegionScene.PhysicsScene, offset, extents);
                        connectedYN = true;
                        break;
                    }
                    */

                    /*
                    // If we're one region over -x +y
                    //xxx
                    //xxx
                    //yxx
                    if ((((int)conn.X * (int)Constants.RegionSize) - conn.XEnd
                        == (regionConnections.X * (int)Constants.RegionSize))
                        && (((int)conn.Y * (int)Constants.RegionSize) - conn.YEnd
                        == (regionConnections.Y * (int)Constants.RegionSize)))
                    {
                        Vector3 offset = Vector3.Zero;
                        offset.X = (((regionConnections.X * (int)Constants.RegionSize)) -
                                    ((conn.X * (int)Constants.RegionSize)));
                        offset.Y = (((regionConnections.Y * (int)Constants.RegionSize)) -
                                    ((conn.Y * (int)Constants.RegionSize)));

                        Vector3 extents = Vector3.Zero;
                        extents.Y = regionConnections.YEnd + conn.YEnd;
                        extents.X = conn.XEnd + conn.XEnd;

                        m_log.DebugFormat("Scene: {0} to the northeast of Scene.  Offset: {2}. Extents:{3}",
                                          conn.RegionScene.RegionInfo.RegionName,
                                          regionConnections.RegionScene.RegionInfo.RegionName, offset, extents);

                        scene.PhysicsScene.Combine(conn.RegionScene.PhysicsScene, offset, extents);


                        connectedYN = true;
                        break;
                    }
                    */

                    /*
                    // If we're one region over -x y
                    //xxx
                    //yxx
                    //xxx
                    if ((((int)conn.X * (int)Constants.RegionSize) - conn.XEnd
                        == (regionConnections.X * (int)Constants.RegionSize))
                        && (((int)conn.Y * (int)Constants.RegionSize)
                        == (regionConnections.Y * (int)Constants.RegionSize)))
                    {
                        Vector3 offset = Vector3.Zero;
                        offset.X = (((regionConnections.X * (int)Constants.RegionSize)) -
                                    ((conn.X * (int)Constants.RegionSize)));
                        offset.Y = (((regionConnections.Y * (int)Constants.RegionSize)) -
                                    ((conn.Y * (int)Constants.RegionSize)));

                        Vector3 extents = Vector3.Zero;
                        extents.Y = regionConnections.YEnd;
                        extents.X = conn.XEnd + conn.XEnd;

                        m_log.DebugFormat("Scene: {0} to the east of Scene{1} Offset: {2}. Extents:{3}",
                                          conn.RegionScene.RegionInfo.RegionName,
                                          regionConnections.RegionScene.RegionInfo.RegionName, offset, extents);

                        scene.PhysicsScene.Combine(conn.RegionScene.PhysicsScene, offset, extents);

                        connectedYN = true;
                        break;
                    }
                    */

                    /*
                        // If we're one region over -x -y
                        //yxx
                        //xxx
                        //xxx
                        if ((((int)conn.X * (int)Constants.RegionSize) - conn.XEnd
                            == (regionConnections.X * (int)Constants.RegionSize))
                            && (((int)conn.Y * (int)Constants.RegionSize) + conn.YEnd
                            == (regionConnections.Y * (int)Constants.RegionSize)))
                        {
                            Vector3 offset = Vector3.Zero;
                            offset.X = (((regionConnections.X * (int)Constants.RegionSize)) -
                                        ((conn.X * (int)Constants.RegionSize)));
                            offset.Y = (((regionConnections.Y * (int)Constants.RegionSize)) -
                                        ((conn.Y * (int)Constants.RegionSize)));

                            Vector3 extents = Vector3.Zero;
                            extents.Y = regionConnections.YEnd + conn.YEnd;
                            extents.X = conn.XEnd + conn.XEnd;

                            m_log.DebugFormat("Scene: {0} to the northeast of Scene{1} Offset: {2}. Extents:{3}",
                                              conn.RegionScene.RegionInfo.RegionName,
                                              regionConnections.RegionScene.RegionInfo.RegionName, offset, extents);

                            scene.PhysicsScene.Combine(conn.RegionScene.PhysicsScene, offset, extents);

                            connectedYN = true;
                            break;
                        }
                        */
                    #endregion

                    // If we're one region over +x y
                    //xxx
                    //xxy
                    //xxx


                    if ((int)conn.X + conn.XEnd
                        >= (regionConnections.X)
                        && ((int)conn.Y)
                        >= (regionConnections.Y))
                    {
                        connectedYN = DoWorkForOneRegionOverPlusXY(conn, regionConnections, scene);
                        break;
                    }

                    // If we're one region over x +y
                    //xyx
                    //xxx
                    //xxx
                    if ((((int)conn.X)
                        >= (regionConnections.X))
                        && (((int)conn.Y) + conn.YEnd
                        >= (regionConnections.Y)))
                    {
                        connectedYN = DoWorkForOneRegionOverXPlusY(conn, regionConnections, scene);
                        break;
                    }

                    // If we're one region over +x +y
                    //xxy
                    //xxx
                    //xxx
                    if ((((int)conn.X) + conn.YEnd
                        >= (regionConnections.X))
                        && (((int)conn.Y) + conn.YEnd
                        >= (regionConnections.Y)))
                    {
                        connectedYN = DoWorkForOneRegionOverPlusXPlusY(conn, regionConnections, scene);
                        break;

                    }
                }

                // If !connectYN means that this region is a root region
                if (!connectedYN)
                {
                    DoWorkForRootRegion(regionConnections, scene);
                }
            }
            // Set up infinite borders around the entire AABB of the combined ConnectedRegions
            AdjustLargeRegionBounds();
        }

        private bool DoWorkForOneRegionOverPlusXY(RegionConnections conn, RegionConnections regionConnections, Scene scene)
        {
            Vector3 offset = Vector3.Zero;
            offset.X = (((regionConnections.X)) -
                        ((conn.X)));
            offset.Y = (((regionConnections.Y)) -
                        ((conn.Y)));

            Vector3 extents = Vector3.Zero;
            extents.Y = conn.YEnd;
            extents.X = conn.XEnd + regionConnections.XEnd;

            conn.UpdateExtents(extents);

            m_log.DebugFormat("Scene: {0} to the west of Scene{1} Offset: {2}. Extents:{3}",
                              conn.RegionScene.RegionInfo.RegionName,
                              regionConnections.RegionScene.RegionInfo.RegionName, offset, extents);

            RegionData ConnectedRegion = new RegionData();
            ConnectedRegion.Offset = offset;
            ConnectedRegion.RegionId = scene.RegionInfo.RegionID;
            ConnectedRegion.RegionScene = scene;
            conn.ConnectedRegions.Add(ConnectedRegion);

            // Inform root region Physics about the extents of this region
            conn.RegionScene.SceneGraph.PhysicsScene.Combine(null, Vector3.Zero, extents);

            // Inform Child region that it needs to forward it's terrain to the root region
            scene.SceneGraph.PhysicsScene.Combine(conn.RegionScene.SceneGraph.PhysicsScene, offset, Vector3.Zero);

            // Reset Terrain..  since terrain loads before we get here, we need to load 
            // it again so it loads in the root region
            ITerrainChannel terrainHeightmap = scene.RequestModuleInterface<ITerrainChannel>();
            scene.SceneGraph.PhysicsScene.SetTerrain(terrainHeightmap.GetFloatsSerialised(scene));

            // Create a client event forwarder and add this region's events to the root region.
            if (conn.ClientEventForwarder != null)
                conn.ClientEventForwarder.AddSceneToEventForwarding(scene);

            landTerrainChannel.AddRegion(ConnectedRegion, terrainHeightmap);
            return true;
        }

        private bool DoWorkForOneRegionOverXPlusY(RegionConnections conn, RegionConnections regionConnections, Scene scene)
        {
            Vector3 offset = Vector3.Zero;
            offset.X = (((regionConnections.X)) -
                        ((conn.X)));
            offset.Y = (((regionConnections.Y)) -
                        ((conn.Y)));

            Vector3 extents = Vector3.Zero;
            extents.Y = regionConnections.YEnd + conn.YEnd;
            extents.X = conn.XEnd;
            conn.UpdateExtents(extents);

            RegionData ConnectedRegion = new RegionData();
            ConnectedRegion.Offset = offset;
            ConnectedRegion.RegionId = scene.RegionInfo.RegionID;
            ConnectedRegion.RegionScene = scene;
            conn.ConnectedRegions.Add(ConnectedRegion);

            m_log.DebugFormat("Scene: {0} to the northeast of Scene{1} Offset: {2}. Extents:{3}",
                             conn.RegionScene.RegionInfo.RegionName,
                             regionConnections.RegionScene.RegionInfo.RegionName, offset, extents);

            conn.RegionScene.SceneGraph.PhysicsScene.Combine(null, Vector3.Zero, extents);
            scene.SceneGraph.PhysicsScene.Combine(conn.RegionScene.SceneGraph.PhysicsScene, offset, Vector3.Zero);

            // Reset Terrain..  since terrain normally loads first.
            //conn.RegionScene.PhysicsScene.SetTerrain(conn.RegionScene.Heightmap.GetFloatsSerialised());
            ITerrainChannel terrainHeightmap = scene.RequestModuleInterface<ITerrainChannel>();
            scene.SceneGraph.PhysicsScene.SetTerrain(terrainHeightmap.GetFloatsSerialised(scene));
            //conn.RegionScene.PhysicsScene.SetTerrain(conn.RegionScene.Heightmap.GetFloatsSerialised());

            if (conn.ClientEventForwarder != null)
                conn.ClientEventForwarder.AddSceneToEventForwarding(scene);
            landTerrainChannel.AddRegion(ConnectedRegion, terrainHeightmap);
            return true;
        }

        private bool DoWorkForOneRegionOverPlusXPlusY(RegionConnections conn, RegionConnections regionConnections, Scene scene)
        {
            Vector3 offset = Vector3.Zero;
            offset.X = (((regionConnections.X)) -
                        ((conn.X)));
            offset.Y = (((regionConnections.Y)) -
                        ((conn.Y)));

            Vector3 extents = Vector3.Zero;
            extents.Y = regionConnections.YEnd + conn.YEnd;
            extents.X = regionConnections.XEnd + conn.XEnd;
            conn.UpdateExtents(extents);

            RegionData ConnectedRegion = new RegionData();
            ConnectedRegion.Offset = offset;
            ConnectedRegion.RegionId = scene.RegionInfo.RegionID;
            ConnectedRegion.RegionScene = scene;

            conn.ConnectedRegions.Add(ConnectedRegion);

            m_log.DebugFormat("Scene: {0} to the NorthEast of Scene{1} Offset: {2}. Extents:{3}",
                             conn.RegionScene.RegionInfo.RegionName,
                             regionConnections.RegionScene.RegionInfo.RegionName, offset, extents);

            conn.RegionScene.SceneGraph.PhysicsScene.Combine(null, Vector3.Zero, extents);
            scene.SceneGraph.PhysicsScene.Combine(conn.RegionScene.SceneGraph.PhysicsScene, offset, Vector3.Zero);
            
            // Reset Terrain..  since terrain normally loads first.
            //conn.RegionScene.PhysicsScene.SetTerrain(conn.RegionScene.Heightmap.GetFloatsSerialised());
            ITerrainChannel terrainHeightmap = scene.RequestModuleInterface<ITerrainChannel>();
            scene.SceneGraph.PhysicsScene.SetTerrain(terrainHeightmap.GetFloatsSerialised(scene));
            //conn.RegionScene.PhysicsScene.SetTerrain(conn.RegionScene.Heightmap.GetFloatsSerialised());
            
            if (conn.ClientEventForwarder != null)
                conn.ClientEventForwarder.AddSceneToEventForwarding(scene);

            landTerrainChannel.AddRegion(ConnectedRegion, terrainHeightmap);

            return true;

            //scene.PhysicsScene.Combine(conn.RegionScene.PhysicsScene, offset,extents);

        }

        private void DoWorkForRootRegion(RegionConnections regionConnections, Scene scene)
        {
            RegionData rdata = new RegionData();
            rdata.Offset = Vector3.Zero;
            rdata.RegionId = scene.RegionInfo.RegionID;
            rdata.RegionScene = scene;
            // save it's land channel
            regionConnections.RegionLandChannel = scene.RequestModuleInterface<IParcelManagementModule>();

            // Substitue our landchannel
            RegionCombinerLargeLandChannel lnd = new RegionCombinerLargeLandChannel(rdata, scene.RequestModuleInterface<IParcelManagementModule>(),
                                                            regionConnections.ConnectedRegions);
            scene.RegisterModuleInterface<IParcelManagementModule>(lnd);
            // Forward the permissions modules of each of the connected regions to the root region
            lock (m_regions)
            {
                foreach (RegionData r in regionConnections.ConnectedRegions)
                {
                    ForwardPermissionRequests(regionConnections, r.RegionScene);
                }
            }
            // Create the root region's Client Event Forwarder
            regionConnections.ClientEventForwarder = new RegionCombinerClientEventForwarder(regionConnections);

            // Sets up the CoarseLocationUpdate forwarder for this root region
            scene.EventManager.OnNewPresence += SetCourseLocationDelegate;

            scene.EventManager.OnNewClient += EventManager_OnNewClient;
            scene.EventManager.OnClosingClient += OnClosingClient;

            // Adds this root region to a dictionary of regions that are connectable
            m_regions.Add(scene.RegionInfo.RegionID, regionConnections);
            ITerrainChannel terrainHeightmap = scene.RequestModuleInterface<ITerrainChannel>();
            landTerrainChannel.AddRegion(rdata, terrainHeightmap);
        }

        void EventManager_OnNewClient(IClientAPI client)
        {
            client.OnSetEstateTerrainDetailTexture += setEstateTerrainBaseTexture;
            client.OnSetEstateTerrainTextureHeights += setEstateTerrainTextureHeights;
            client.OnSetRegionTerrainSettings += setRegionTerrainSettings;
        }

        private void OnClosingClient(IClientAPI client)
        {
            client.OnSetEstateTerrainDetailTexture -= setEstateTerrainBaseTexture;
            client.OnSetEstateTerrainTextureHeights -= setEstateTerrainTextureHeights;
            client.OnSetRegionTerrainSettings -= setRegionTerrainSettings;
        }

        public void setRegionTerrainSettings(UUID AgentID, float WaterHeight,
                float TerrainRaiseLimit, float TerrainLowerLimit,
                bool UseEstateSun, bool UseFixedSun, float SunHour,
                bool UseGlobal, bool EstateFixedSun, float EstateSunHour)
        {
            lock (m_regions)
            {
                foreach (Scene r in m_startingScenes.Values)
                {
                    if (AgentID == UUID.Zero || r.Permissions.CanIssueEstateCommand(AgentID, false))
                    {
                        // Water Height
                        r.RegionInfo.RegionSettings.WaterHeight = WaterHeight;

                        // Terraforming limits
                        r.RegionInfo.RegionSettings.TerrainRaiseLimit = TerrainRaiseLimit;
                        r.RegionInfo.RegionSettings.TerrainLowerLimit = TerrainLowerLimit;

                        // Time of day / fixed sun
                        r.RegionInfo.RegionSettings.UseEstateSun = UseEstateSun;
                        r.RegionInfo.RegionSettings.FixedSun = UseFixedSun;
                        r.RegionInfo.RegionSettings.SunPosition = SunHour;

                        r.RequestModuleInterface<IEstateModule>().TriggerEstateSunUpdate();

                        //m_log.Debug("[ESTATE]: UFS: " + UseFixedSun.ToString());
                        //m_log.Debug("[ESTATE]: SunHour: " + SunHour.ToString());

                        sendRegionInfoPacketToAll(r);
                        r.RegionInfo.RegionSettings.Save();
                        r.RequestModuleInterface<IEstateModule>().sendRegionHandshakeToAll();
                    }
                }
            }
        }

        public void setEstateTerrainBaseTexture(IClientAPI remoteClient, int corner, UUID texture)
        {
            foreach (Scene r in m_startingScenes.Values)
            {
                if (texture == UUID.Zero)
                    return;

                switch (corner)
                {
                    case 0:
                        r.RegionInfo.RegionSettings.TerrainTexture1 = texture;
                        break;
                    case 1:
                        r.RegionInfo.RegionSettings.TerrainTexture2 = texture;
                        break;
                    case 2:
                        r.RegionInfo.RegionSettings.TerrainTexture3 = texture;
                        break;
                    case 3:
                        r.RegionInfo.RegionSettings.TerrainTexture4 = texture;
                        break;
                }
                r.RegionInfo.RegionSettings.Save();
                r.RequestModuleInterface<IEstateModule>().sendRegionHandshakeToAll();
                sendRegionInfoPacketToAll(r);
            }
        }

        public void setEstateTerrainTextureHeights(IClientAPI remoteClient, int corner, float lowValue, float highValue)
        {
            foreach (Scene r in m_startingScenes.Values)
            {
                switch (corner)
                {
                    case 0:
                        r.RegionInfo.RegionSettings.Elevation1SW = lowValue;
                        r.RegionInfo.RegionSettings.Elevation2SW = highValue;
                        break;
                    case 1:
                        r.RegionInfo.RegionSettings.Elevation1NW = lowValue;
                        r.RegionInfo.RegionSettings.Elevation2NW = highValue;
                        break;
                    case 2:
                        r.RegionInfo.RegionSettings.Elevation1SE = lowValue;
                        r.RegionInfo.RegionSettings.Elevation2SE = highValue;
                        break;
                    case 3:
                        r.RegionInfo.RegionSettings.Elevation1NE = lowValue;
                        r.RegionInfo.RegionSettings.Elevation2NE = highValue;
                        break;
                }
                r.RegionInfo.RegionSettings.Save();
                r.RequestModuleInterface<IEstateModule>().sendRegionHandshakeToAll();
                sendRegionInfoPacketToAll(r);
            }
        }

        private void sendRegionInfoPacketToAll(Scene scene)
        {
            scene.ForEachScenePresence(delegate(ScenePresence sp)
            {
                if (!sp.IsChildAgent)
                    HandleRegionInfoRequest(sp.ControllingClient, scene);
            });
        }

        private void HandleRegionInfoRequest(IClientAPI remote_client, Scene m_scene)
        {
            RegionInfoForEstateMenuArgs args = new RegionInfoForEstateMenuArgs();
            args.billableFactor = m_scene.RegionInfo.EstateSettings.BillableFactor;
            args.estateID = m_scene.RegionInfo.EstateSettings.EstateID;
            args.maxAgents = (byte)m_scene.RegionInfo.RegionSettings.AgentLimit;
            args.objectBonusFactor = (float)m_scene.RegionInfo.RegionSettings.ObjectBonus;
            args.parentEstateID = m_scene.RegionInfo.EstateSettings.ParentEstateID;
            args.pricePerMeter = m_scene.RegionInfo.EstateSettings.PricePerMeter;
            args.redirectGridX = m_scene.RegionInfo.EstateSettings.RedirectGridX;
            args.redirectGridY = m_scene.RegionInfo.EstateSettings.RedirectGridY;
            args.regionFlags = GetRegionFlags(m_scene);
            args.simAccess = m_scene.RegionInfo.AccessLevel;
            args.sunHour = (float)m_scene.RegionInfo.RegionSettings.SunPosition;
            args.terrainLowerLimit = (float)m_scene.RegionInfo.RegionSettings.TerrainLowerLimit;
            args.terrainRaiseLimit = (float)m_scene.RegionInfo.RegionSettings.TerrainRaiseLimit;
            args.useEstateSun = m_scene.RegionInfo.RegionSettings.UseEstateSun;
            args.waterHeight = (float)m_scene.RegionInfo.RegionSettings.WaterHeight;
            args.simName = m_scene.RegionInfo.RegionName;
            args.regionType = m_scene.RegionInfo.RegionType;

            remote_client.SendRegionInfoToEstateMenu(args);
        }

        public uint GetRegionFlags(Scene m_scene)
        {
            RegionFlags flags = RegionFlags.None;

            // Fully implemented
            //
            if (m_scene.RegionInfo.RegionSettings.AllowDamage)
                flags |= RegionFlags.AllowDamage;
            if (m_scene.RegionInfo.RegionSettings.BlockTerraform)
                flags |= RegionFlags.BlockTerraform;
            if (!m_scene.RegionInfo.RegionSettings.AllowLandResell)
                flags |= RegionFlags.BlockLandResell;
            if (m_scene.RegionInfo.RegionSettings.DisableCollisions)
                flags |= RegionFlags.SkipCollisions;
            if (m_scene.RegionInfo.RegionSettings.DisableScripts)
                flags |= RegionFlags.SkipScripts;
            if (m_scene.RegionInfo.RegionSettings.DisablePhysics)
                flags |= RegionFlags.SkipPhysics;
            if (m_scene.RegionInfo.RegionSettings.BlockFly)
                flags |= RegionFlags.NoFly;
            if (m_scene.RegionInfo.RegionSettings.RestrictPushing)
                flags |= RegionFlags.RestrictPushObject;
            if (m_scene.RegionInfo.RegionSettings.AllowLandJoinDivide)
                flags |= RegionFlags.AllowParcelChanges;
            if (m_scene.RegionInfo.RegionSettings.BlockShowInSearch)
                flags |= (RegionFlags)(1 << 29);

            if (m_scene.RegionInfo.RegionSettings.FixedSun)
                flags |= RegionFlags.SunFixed;
            if (m_scene.RegionInfo.RegionSettings.Sandbox)
                flags |= RegionFlags.Sandbox;

            // Fudge these to always on, so the menu options activate
            //
            flags |= RegionFlags.AllowLandmark;
            flags |= RegionFlags.AllowSetHome;

            // Omitted
            //
            // Omitted: SkipUpdateInterestList (what does it do?)
            // Omitted: NullLayer (what is that?)
            // Omitted: SkipAgentAction (what does it do?)

            return (uint)flags;
        }

        private void SetCourseLocationDelegate(ScenePresence presence)
        {
            presence.SetSendCourseLocationMethod(SendCourseLocationUpdates);
        }

        // This delegate was refactored for non-combined regions.
        // This combined region version will not use the pre-compiled lists of locations and ids
        private void SendCourseLocationUpdates(UUID sceneId, ScenePresence presence, List<Vector3> coarseLocations, List<UUID> avatarUUIDs)
        {
            RegionConnections connectiondata = null; 
            lock (m_regions)
            {
                if (m_regions.ContainsKey(sceneId))
                    connectiondata = m_regions[sceneId];
                else
                    return;
            }

            List<Vector3> CoarseLocations = new List<Vector3>();
            List<UUID> AvatarUUIDs = new List<UUID>();
            connectiondata.RegionScene.ForEachScenePresence(delegate(ScenePresence sp)
            {
                if (sp.IsChildAgent)
                    return;
                //if (sp.UUID != presence.UUID)
                //{
                    if (sp.ParentID != UUID.Zero)
                    {
                        // sitting avatar
                        SceneObjectPart sop = connectiondata.RegionScene.GetSceneObjectPart(sp.ParentID);
                        if (sop != null)
                        {
                            CoarseLocations.Add(sop.AbsolutePosition + sp.AbsolutePosition);
                            AvatarUUIDs.Add(sp.UUID);
                        }
                        else
                        {
                            // we can't find the parent..  ! arg!
                            CoarseLocations.Add(sp.AbsolutePosition);
                            AvatarUUIDs.Add(sp.UUID);
                        }
                    }
                    else
                    {
                        CoarseLocations.Add(sp.AbsolutePosition);
                        AvatarUUIDs.Add(sp.UUID);
                    }
                //}
            });
            DistributeCourseLocationUpdates(CoarseLocations, AvatarUUIDs, connectiondata, presence);
        }

        private void DistributeCourseLocationUpdates(List<Vector3> locations, List<UUID> uuids, 
                                                     RegionConnections connectiondata, ScenePresence rootPresence)
        {
            RegionData[] rdata = connectiondata.ConnectedRegions.ToArray();
            //List<IClientAPI> clients = new List<IClientAPI>();
            Dictionary<Vector2, RegionCourseLocationStruct> updates = new Dictionary<Vector2, RegionCourseLocationStruct>();
            
            // Root Region entry
            RegionCourseLocationStruct rootupdatedata = new RegionCourseLocationStruct();
            rootupdatedata.Locations = new List<Vector3>();
            rootupdatedata.Uuids = new List<UUID>();
            rootupdatedata.Offset = Vector2.Zero;

            rootupdatedata.UserAPI = rootPresence.ControllingClient;

            if (rootupdatedata.UserAPI != null)
                updates.Add(Vector2.Zero, rootupdatedata);

            //Each Region needs an entry or we will end up with dead minimap dots
            foreach (RegionData regiondata in rdata)
            {
                Vector2 offset = new Vector2(regiondata.Offset.X, regiondata.Offset.Y);
                RegionCourseLocationStruct updatedata = new RegionCourseLocationStruct();
                updatedata.Locations = new List<Vector3>();
                updatedata.Uuids = new List<UUID>();
                updatedata.Offset = offset;

                if (offset == Vector2.Zero)
                    updatedata.UserAPI = rootPresence.ControllingClient;
                else
                    updatedata.UserAPI = LocateUsersChildAgentIClientAPI(offset, rootPresence.UUID, rdata);

                if (updatedata.UserAPI != null)
                    updates.Add(offset, updatedata);
            }

            // go over the locations and assign them to an IClientAPI
            for (int i = 0; i < locations.Count; i++)
            //{locations[i]/(int) Constants.RegionSize;
            {
                Vector3 pPosition = new Vector3((int)locations[i].X / (int)Constants.RegionSize, 
                                                (int)locations[i].Y / (int)Constants.RegionSize, locations[i].Z);
                Vector2 offset = new Vector2(pPosition.X*(int) Constants.RegionSize,
                                             pPosition.Y*(int) Constants.RegionSize);
                
                if (!updates.ContainsKey(offset))
                {
                    // This shouldn't happen
                    RegionCourseLocationStruct updatedata = new RegionCourseLocationStruct();
                    updatedata.Locations = new List<Vector3>();
                    updatedata.Uuids = new List<UUID>();
                    updatedata.Offset = offset;
                    
                    if (offset == Vector2.Zero)
                        updatedata.UserAPI = rootPresence.ControllingClient;
                    else 
                        updatedata.UserAPI = LocateUsersChildAgentIClientAPI(offset, rootPresence.UUID, rdata);

                    updates.Add(offset,updatedata);
                }
                updates[offset].Locations.Add(locations[i]);
                updates[offset].Uuids.Add(uuids[i]);
            }

            // Send out the CoarseLocationupdates from their respective client connection based on where the avatar is
            foreach (Vector2 offset in updates.Keys)
            {
                if (updates[offset].UserAPI != null)
                {
                    updates[offset].UserAPI.SendCoarseLocationUpdate(updates[offset].Uuids,updates[offset].Locations);
                }
            }
        }

        /// <summary>
        /// Locates a the Client of a particular region in an Array of RegionData based on offset
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="uUID"></param>
        /// <param name="rdata"></param>
        /// <returns>IClientAPI or null</returns>
        private IClientAPI LocateUsersChildAgentIClientAPI(Vector2 offset, UUID uUID, RegionData[] rdata)
        {
            foreach (RegionData r in rdata)
            {
                if (r.Offset.X == offset.X && r.Offset.Y == offset.Y)
                {
                    if(r.RegionScene.GetScenePresence(uUID) != null)
                        return r.RegionScene.GetScenePresence(uUID).ControllingClient;
                }
            }

            return null;
        }

        public void PostInitialise()
        {
        }
        
        /// <summary>
        /// TODO: UnCombineRegion
        /// </summary>
        /// <param name="rdata"></param>
        public void UnCombineRegion(RegionData rdata)
        {
            lock (m_regions)
            {
                if (m_regions.ContainsKey(rdata.RegionId))
                {
                    // uncombine root region and virtual regions
                }
                else
                {
                    foreach (RegionConnections r in m_regions.Values)
                    {
                        foreach (RegionData rd in r.ConnectedRegions)
                        {
                            if (rd.RegionId == rdata.RegionId)
                            {
                                // uncombine virtual region
                            }
                        }
                    }
                }
            }
        }

        // Create a set of infinite borders around the whole aabb of the combined island.
        private void AdjustLargeRegionBounds()
        {
            lock (m_regions)
            {
                //For each region, we have to adjust the region size so that it includes all the other regions
                //  So to do this, we'll first find the corners of the rectangle we are going to create,
                //  then fix the sizes for all the regions.
                //  Note: all child regions get infinite borders

                foreach (RegionConnections rconn in m_regions.Values) //This is normally just the 'root' region, but is stubbed out for multiple 'root' regions
                {
                    Vector2 offset = Vector2.Zero;
                    //Find the largest offset of the connected regions to find the corners of our rectangle
                    foreach (RegionData rdata in rconn.ConnectedRegions)
                    {
                        if (rdata.Offset.X > offset.X) offset.X = rdata.Offset.X;
                        if (rdata.Offset.Y > offset.Y) offset.Y = rdata.Offset.Y;

                        //Infinite so that they cannot have crossings as the root will have to deal with that
                        rdata.RegionScene.RegionInfo.RegionSizeX = int.MaxValue;
                        rdata.RegionScene.RegionInfo.RegionSizeY = int.MaxValue;
                    }

                    //Add the default region size + the offset so that we get the full rectangle
                    rconn.RegionScene.RegionInfo.RegionSizeX = Constants.RegionSize + (int)offset.X;
                    rconn.RegionScene.RegionInfo.RegionSizeY = Constants.RegionSize + (int)offset.Y;
                }
            }
        }
       
        public RegionData GetRegionFromPosition(Vector3 pPosition)
        {
            pPosition = pPosition/(int) Constants.RegionSize;
            int OffsetX = (int) pPosition.X;
            int OffsetY = (int) pPosition.Y;
            foreach (RegionConnections regConn in m_regions.Values)
            {
                foreach (RegionData reg in regConn.ConnectedRegions)
                {
                    if (reg.Offset.X == OffsetX && reg.Offset.Y == OffsetY)
                        return reg;
                }
            }
            return new RegionData();
        }

        public void ForwardPermissionRequests(RegionConnections BigRegion, Scene VirtualRegion)
        {
            if (BigRegion.PermissionModule == null)
                BigRegion.PermissionModule = new RegionCombinerPermissionModule(BigRegion.RegionScene);

            VirtualRegion.Permissions.OnBypassPermissions += BigRegion.PermissionModule.BypassPermissions;
            VirtualRegion.Permissions.OnSetBypassPermissions += BigRegion.PermissionModule.SetBypassPermissions;
            VirtualRegion.Permissions.OnPropagatePermissions += BigRegion.PermissionModule.PropagatePermissions;
            VirtualRegion.Permissions.OnGenerateClientFlags += BigRegion.PermissionModule.GenerateClientFlags;
            VirtualRegion.Permissions.OnAbandonParcel += BigRegion.PermissionModule.CanAbandonParcel;
            VirtualRegion.Permissions.OnReclaimParcel += BigRegion.PermissionModule.CanReclaimParcel;
            VirtualRegion.Permissions.OnDeedParcel += BigRegion.PermissionModule.CanDeedParcel;
            VirtualRegion.Permissions.OnDeedObject += BigRegion.PermissionModule.CanDeedObject;
            VirtualRegion.Permissions.OnIsGod += BigRegion.PermissionModule.IsGod;
            VirtualRegion.Permissions.OnDuplicateObject += BigRegion.PermissionModule.CanDuplicateObject;
            VirtualRegion.Permissions.OnDeleteObject += BigRegion.PermissionModule.CanDeleteObject; //MAYBE FULLY IMPLEMENTED
            VirtualRegion.Permissions.OnEditObject += BigRegion.PermissionModule.CanEditObject; //MAYBE FULLY IMPLEMENTED
            VirtualRegion.Permissions.OnEditParcel += BigRegion.PermissionModule.CanEditParcel; //MAYBE FULLY IMPLEMENTED
            VirtualRegion.Permissions.OnInstantMessage += BigRegion.PermissionModule.CanInstantMessage;
            VirtualRegion.Permissions.OnInventoryTransfer += BigRegion.PermissionModule.CanInventoryTransfer; //NOT YET IMPLEMENTED
            VirtualRegion.Permissions.OnIssueEstateCommand += BigRegion.PermissionModule.CanIssueEstateCommand; //FULLY IMPLEMENTED
            VirtualRegion.Permissions.OnMoveObject += BigRegion.PermissionModule.CanMoveObject; //MAYBE FULLY IMPLEMENTED
            VirtualRegion.Permissions.OnObjectEntry += BigRegion.PermissionModule.CanObjectEntry;
            VirtualRegion.Permissions.OnReturnObjects += BigRegion.PermissionModule.CanReturnObjects; //NOT YET IMPLEMENTED
            VirtualRegion.Permissions.OnRezObject += BigRegion.PermissionModule.CanRezObject; //MAYBE FULLY IMPLEMENTED
            VirtualRegion.Permissions.OnRunConsoleCommand += BigRegion.PermissionModule.CanRunConsoleCommand;
            VirtualRegion.Permissions.OnRunScript += BigRegion.PermissionModule.CanRunScript; //NOT YET IMPLEMENTED
            VirtualRegion.Permissions.OnCompileScript += BigRegion.PermissionModule.CanCompileScript;
            VirtualRegion.Permissions.OnSellParcel += BigRegion.PermissionModule.CanSellParcel;
            VirtualRegion.Permissions.OnTakeObject += BigRegion.PermissionModule.CanTakeObject;
            VirtualRegion.Permissions.OnTakeCopyObject += BigRegion.PermissionModule.CanTakeCopyObject;
            VirtualRegion.Permissions.OnTerraformLand += BigRegion.PermissionModule.CanTerraformLand;
            VirtualRegion.Permissions.OnLinkObject += BigRegion.PermissionModule.CanLinkObject; //NOT YET IMPLEMENTED
            VirtualRegion.Permissions.OnDelinkObject += BigRegion.PermissionModule.CanDelinkObject; //NOT YET IMPLEMENTED
            VirtualRegion.Permissions.OnBuyLand += BigRegion.PermissionModule.CanBuyLand; //NOT YET IMPLEMENTED
            VirtualRegion.Permissions.OnViewNotecard += BigRegion.PermissionModule.CanViewNotecard; //NOT YET IMPLEMENTED
            VirtualRegion.Permissions.OnViewScript += BigRegion.PermissionModule.CanViewScript; //NOT YET IMPLEMENTED
            VirtualRegion.Permissions.OnEditNotecard += BigRegion.PermissionModule.CanEditNotecard; //NOT YET IMPLEMENTED
            VirtualRegion.Permissions.OnEditScript += BigRegion.PermissionModule.CanEditScript; //NOT YET IMPLEMENTED
            VirtualRegion.Permissions.OnCreateObjectInventory += BigRegion.PermissionModule.CanCreateObjectInventory; //NOT IMPLEMENTED HERE 
            VirtualRegion.Permissions.OnEditObjectInventory += BigRegion.PermissionModule.CanEditObjectInventory;//MAYBE FULLY IMPLEMENTED
            VirtualRegion.Permissions.OnCopyObjectInventory += BigRegion.PermissionModule.CanCopyObjectInventory; //NOT YET IMPLEMENTED
            VirtualRegion.Permissions.OnDeleteObjectInventory += BigRegion.PermissionModule.CanDeleteObjectInventory; //NOT YET IMPLEMENTED
            VirtualRegion.Permissions.OnResetScript += BigRegion.PermissionModule.CanResetScript;
            VirtualRegion.Permissions.OnCreateUserInventory += BigRegion.PermissionModule.CanCreateUserInventory; //NOT YET IMPLEMENTED
            VirtualRegion.Permissions.OnCopyUserInventory += BigRegion.PermissionModule.CanCopyUserInventory; //NOT YET IMPLEMENTED
            VirtualRegion.Permissions.OnEditUserInventory += BigRegion.PermissionModule.CanEditUserInventory; //NOT YET IMPLEMENTED
            VirtualRegion.Permissions.OnDeleteUserInventory += BigRegion.PermissionModule.CanDeleteUserInventory; //NOT YET IMPLEMENTED
        }

        #region console commands

        public void FixPhantoms(string module, string[] cmdparams)
        {
        List<Scene> scenes = new List<Scene>(m_startingScenes.Values);
            foreach (Scene s in scenes)
            {
                s.ForEachSOG(delegate(SceneObjectGroup e)
                {
                    e.AbsolutePosition = e.AbsolutePosition;
                }
                );
            }
        }

        #endregion
    }
}
