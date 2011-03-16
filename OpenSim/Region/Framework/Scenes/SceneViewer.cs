﻿/*
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
using System.Text;
using System.Threading;
using OpenMetaverse;
using log4net;
using OpenSim.Framework;
using OpenSim.Framework.Client;
using OpenSim.Region.Framework.Interfaces;
using Mischel.Collections;

namespace OpenSim.Region.Framework.Scenes
{
    public class SceneViewer : ISceneViewer
    {
        #region Declares

        private const double MINVIEWDSTEP = 16;
        private const double MINVIEWDSTEPSQ = MINVIEWDSTEP * MINVIEWDSTEP;

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected IScenePresence m_presence;
//        protected PriorityQueue<EntityUpdate, double> m_partsUpdateQueue;
        /// <summary>
        /// Param 1 - LocalID of the object, Param 2 - The last version when this was added to the list
        /// </summary>
//        protected Dictionary<uint, int> m_removeNextUpdateOf = new Dictionary<uint, int>();
//        protected List<uint> m_removeUpdateOf = new List<uint>();
        protected bool m_SentInitialObjects = false;
        protected volatile bool m_queueing =false;
        protected volatile bool m_inUse = false;
//        protected volatile bool m_updatesNeedReprioritization = false;
//        protected volatile List<UUID> m_objectsInView = new List<UUID>();
        protected Prioritizer m_prioritizer;
//        private readonly Mutex _versionAllocateMutex = new Mutex(false);
//        protected int m_lastVersion = 0;
        private Queue<object> m_delayedUpdates = new Queue<object>();

        private HashSet<SceneObjectGroup> lastGrpsInView = new HashSet<SceneObjectGroup>();

        private Vector3 m_lastUpdatePos;
        private float lastDrawDistance;
        public Prioritizer Prioritizer
        {
            get { return m_prioritizer; }
        }

        #endregion

        #region Constructor

        public SceneViewer (IScenePresence presence)
        {
            m_presence = presence;
            m_presence.Scene.EventManager.OnSignificantClientMovement += SignificantClientMovement;
            m_prioritizer = new Prioritizer(presence.Scene);
//            m_partsUpdateQueue = new PriorityQueue<EntityUpdate, double>(presence.Scene.Entities.Count > 1000 ? presence.Scene.Entities.Count + 1000 : 1000);
        }

        #endregion

        #region Enqueue/Remove updates for objects

        private double calcMinPrio()
            {
            double p = -m_presence.DrawDistance * m_presence.DrawDistance;
            if (p == 0)
                p = -1024;
            p -= MINVIEWDSTEPSQ;
            return p;
            }

        /// <summary>
        /// Add the objects to the queue for which we need to send an update to the client
        /// </summary>
        /// <param name="part"></param>
        public void QueuePartForUpdate(SceneObjectPart part, PrimUpdateFlags UpdateFlags)
            {
/*            EntityUpdate update = new EntityUpdate(part, UpdateFlags);
            bool ignore;
            lock (m_partsUpdateQueue)
                {
                ignore = m_partsUpdateQueue.Contains(update);
                }
            if (ignore)
                return;
*/
            if (m_presence.Scene.CheckForObjectCulling)
                {
                // priority is negative of distance
                double priority = m_prioritizer.GetUpdatePriority(m_presence.ControllingClient, part.ParentGroup);
                if (priority < calcMinPrio())
                    return; // if 2 far ignore
                }

            lock (m_delayedUpdates)
                {
                if (m_queueing)
                    {
                    object[] o = new object[] { part, UpdateFlags };
                    m_delayedUpdates.Enqueue(o);
                    return;
                    }
                }
            SendUpdate(part, m_presence.GenerateClientFlags(part), UpdateFlags);

            /*
            double priority = m_prioritizer.GetUpdatePriority(m_presence.ControllingClient, part.ParentGroup);
            EntityUpdate update = new EntityUpdate(part, UpdateFlags);
            //Fix the version with the newest locked m_version
            FixVersion(update);
            PriorityQueueItem<EntityUpdate, double> item = new PriorityQueueItem<EntityUpdate, double>();
            item.Priority = priority;
            item.Value = update;
            m_partsUpdateQueue.Enqueue(item);

            //Make it check when the user comes around to it again
            if (m_objectsInView.Contains(part.UUID))
                m_objectsInView.Remove(part.UUID);
             */
        }

        /// <summary>
        /// Updates the last version securely and set the update's version correctly
        /// </summary>
        /// <param name="update"></param>
/*
        private void FixVersion(EntityUpdate update)
        {
            _versionAllocateMutex.WaitOne();
            m_lastVersion++;
            update.Version = m_lastVersion;
            _versionAllocateMutex.ReleaseMutex();
        }

        /// <summary>
        /// Get the current version securely as we lock the mutex for it
        /// </summary>
        /// <returns></returns>
        private int GetVersion()
        {
            int version;

            _versionAllocateMutex.WaitOne();
            version = m_lastVersion;
            _versionAllocateMutex.ReleaseMutex();

            return version;
        }
*/
        /// <summary>
        /// Clear the updates for this part in the next update loop
        /// </summary>
        /// <param name="part"></param>
        public void ClearUpdatesForPart(SceneObjectPart part)
        {
/*
            lock (m_removeUpdateOf)
            {
                //Add it to the list to check and make sure that we do not send updates for this object
                m_removeUpdateOf.Add(part.LocalId);
                //Make it check when the user comes around to it again
                if (m_objectsInView.Contains(part.UUID))
                    m_objectsInView.Remove(part.UUID);
            }
 */
        }

        /// <summary>
        /// Clear the updates for this part in the next update loop
        /// </summary>
        /// <param name="part"></param>
        public void ClearUpdatesForOneLoopForPart(SceneObjectPart part)
        {
/*
            lock (m_removeNextUpdateOf)
            {
                //Add it to the list to check and make sure that we do not send updates for this object
                m_removeNextUpdateOf.Add(part.LocalId, GetVersion());
                //Make it check when the user comes around to it again
                if (m_objectsInView.Contains(part.UUID))
                    m_objectsInView.Remove(part.UUID);
            }
 */
        }

        /// <summary>
        /// Run through all of the updates we have and re-assign their priority depending
        ///  on what is now going on in the Scene
        /// </summary>
        public void Reprioritize()
        {
//            m_updatesNeedReprioritization = true;
        }

        #endregion

        #region Object Culling by draw distance

        /// <summary>
        /// When the client moves enough to trigger this, make sure that we have sent
        ///  the client all of the objects that have just entered their FOV in their draw distance.
        /// </summary>
        /// <param name="remote_client"></param>
        private void SignificantClientMovement(IClientAPI remote_client)
            {
            if (!m_presence.Scene.CheckForObjectCulling)
                return;

            //Only check our presence
            if (remote_client.AgentId != m_presence.UUID)
                return;

            if (m_presence.DrawDistance == 0)
                return;

            

            //If the draw distance is 0, the client has gotten messed up or something and we can't do this...
            if (!m_presence.IsChildAgent || (m_presence.Scene.RegionInfo.SeeIntoThisSimFromNeighbor))
                {
                Vector3 pos = m_presence.CameraPosition;
                float distsq = Vector3.DistanceSquared(pos, m_lastUpdatePos);
                distsq += 0.2f * m_presence.Velocity.LengthSquared();
                if (lastDrawDistance == m_presence.DrawDistance && distsq < MINVIEWDSTEPSQ)
                    return;

                if (
                    lastDrawDistance > m_presence.Scene.RegionInfo.RegionSizeX &&
                    lastDrawDistance > m_presence.Scene.RegionInfo.RegionSizeY
                    )
                    {
                    lastDrawDistance = m_presence.DrawDistance;
                    lastGrpsInView.Clear();
                    return;
                    }

                lock (m_delayedUpdates)
                    {
                    if (m_queueing)
                        return;
                    m_queueing = true;
                    Util.FireAndForget(DoSignificantClientMovement);
                    }
                }
            }

        private void DoSignificantClientMovement(object o)
            {
                ISceneEntity[] entities = m_presence.Scene.Entities.GetEntities ();
            PriorityQueue<EntityUpdate, double> m_entsqueue = new PriorityQueue<EntityUpdate, double>(entities.Length);

            double newminp = calcMinPrio() - 0.2*m_presence.Velocity.LengthSquared();;

            // build a prioritized list of things we need to send

            HashSet<SceneObjectGroup> NewGrpsInView = new HashSet<SceneObjectGroup>();

            foreach (ISceneEntity e in entities)
                {
                if (e != null && e is SceneObjectGroup)
                    {
                    if (e.IsDeleted)
                        continue;

                    double priority = m_prioritizer.GetUpdatePriority(m_presence.ControllingClient, e);

                    //Check for culling here!
                    if (priority < newminp)
                        continue; // if 2 far ignore

                    NewGrpsInView.Add((SceneObjectGroup)e);

                    if (lastGrpsInView.Contains((SceneObjectGroup)e))
                        continue;

                    EntityUpdate update = new EntityUpdate(e, PrimUpdateFlags.FullUpdate);
                    PriorityQueueItem<EntityUpdate, double> item = new PriorityQueueItem<EntityUpdate, double>();
                    item.Value = update;
                    item.Priority = priority;
                    m_entsqueue.Enqueue(item);
                    }
                }
            entities = null;
            lastGrpsInView.Clear();
            lastGrpsInView.UnionWith(NewGrpsInView);
            NewGrpsInView.Clear();

            // send them 
            SendQueued(m_entsqueue);
            }
/*
            //This checks to see if we need to send more updates to the avatar since they last moved
            EntityBase[] Entities = m_presence.Scene.Entities.GetEntities();

            foreach (EntityBase entity in Entities)
            {
                if (entity is SceneObjectGroup) //Only objects
                {
                    //Check to see if they are in range

                    if (CheckForCulling(entity))
                    {
                        //Check if we already have sent them an update
                        if (!m_objectsInView.Contains(entity.UUID))
                        {
                            //Update the list
                            m_objectsInView.Add(entity.UUID);
                            //Send the update
                            SendUpdate(PrimUpdateFlags.FullUpdate, (SceneObjectGroup)entity); //New object, send full
                        }
                    }
                }
            }
            Entities = null;
        }
*/
        /// <summary>
        /// Checks to see whether the object should be sent to the client
        /// Returns true if the client should be able to see the object, false if not
        /// </summary>
        /// <param name="grp"></param>
        /// <returns></returns>
/*
        private bool CheckForCulling(ISceneEntity grp)
        {
            if (!m_presence.Scene.CheckForObjectCulling)
                return true;
            if (m_presence.DrawDistance > 0)

                {
                Vector3 gprpos = grp.AbsolutePosition; // not doing this was making several calls
                Vector3 grphsize = grp.GroupScale(); // and this is heavy someone needs to look to it and cache for static prims
                Quaternion grprot = grp.GroupRotation;

                grphsize = grphsize * grprot; // prims are rotated in world
                grphsize = grphsize * 0.5f; // save same cpu in the 2 calls
                //Check for part position against the av and the camera position
                if (CheckCullingAgainstPosition(m_presence.AbsolutePosition, gprpos, grphsize) ||
                    CheckCullingAgainstPosition(m_presence.CameraPosition, gprpos, grphsize))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks to see whether the client should be able to see the given object from the given position
        /// </summary>
        /// <param name="checkPosition"></param>
        /// <param name="groupPosition"></param>
        /// <param name="groupSize"></param>
        /// <returns></returns>
        private bool CheckCullingAgainstPosition(Vector3 checkPosition, Vector3 groupPosition, Vector3 groupHalfSizeRotated)
        {
            //First just check against the position 
            // not good prims can be huge and also distance calls sqrt and that takes more time than all the rest of this code
//            if (Util.DistanceLessThan(checkPosition, groupPosition, m_presence.DrawDistance))
//                return true;
            //Next, start checking aginst the group corners
            //NOTE: This really should be checking as a sphere.. but that hasn't been done yet

            // no check grp AABB against a distance cube


            if (Math.Abs(checkPosition.X - groupPosition.X) - groupHalfSizeRotated.X < m_presence.DrawDistance)
                return true;
            if (Math.Abs(checkPosition.Y - groupPosition.Y) - groupHalfSizeRotated.Y < m_presence.DrawDistance)
                return true;
            if (Math.Abs(checkPosition.Z - groupPosition.Z) - groupHalfSizeRotated.Z < m_presence.DrawDistance)
                return true;
            //All done then...
            return false;
        }
*/
        #endregion

        #region SendPrimUpdates

        /// <summary>
        /// This loops through all of the lists that we have for the client
        ///  as well as checking whether the client has ever entered the sim before
        ///  and sending the needed updates to them if they have just entered.
        ///  NOTE: This does add the updates to the LLUDPClient queue, it does NOT have prioritization built in before this method!
        /// </summary>
        public void SendPrimUpdates()
            {
            if (m_inUse)
                return;
            m_inUse = true;
            //This is for stats
            int AgentMS = Util.EnvironmentTickCount();

            #region New client entering the Scene, requires all objects in the Scene

            ///If we havn't started processing this client yet, we need to send them ALL the prims that we have in this Scene (and deal with culling as well...)
            if (!m_SentInitialObjects)
                {
                m_SentInitialObjects = true;
                //If they are not in this region, we check to make sure that we allow seeing into neighbors
                if (!m_presence.IsChildAgent || (m_presence.Scene.RegionInfo.SeeIntoThisSimFromNeighbor))
                    {
                    lock (m_delayedUpdates)
                        {
                        m_queueing = true;
                        }
                    double minp = calcMinPrio();
                    bool doculling = m_presence.Scene.CheckForObjectCulling;

                    if (doculling &&
                        m_presence.DrawDistance > m_presence.Scene.RegionInfo.RegionSizeX &&
                        m_presence.DrawDistance > m_presence.Scene.RegionInfo.RegionSizeY
                        )
                        doculling = false;


                    ISceneEntity[] entities = m_presence.Scene.Entities.GetEntities ();
                    PriorityQueue<EntityUpdate, double> m_entsqueue = new PriorityQueue<EntityUpdate, double>(entities.Length);

                    // build a prioritized list of things we need to send

                    foreach (ISceneEntity e in entities)
                        {
                        if (e != null && e is SceneObjectGroup)
                            {
                            if (e.IsDeleted)
                                continue;

                            double priority = m_prioritizer.GetUpdatePriority(m_presence.ControllingClient, e);

                            //Check for culling here!
                            if (doculling)
                                {
                                // priority is negative of distance
                                if (priority < minp)
                                    continue; // if 2 far ignore
                                lastGrpsInView.Add((SceneObjectGroup)e);
                                }                          

                            EntityUpdate update = new EntityUpdate(e, PrimUpdateFlags.FullUpdate);
                            PriorityQueueItem<EntityUpdate, double> item = new PriorityQueueItem<EntityUpdate, double>();
                            item.Value = update;
                            item.Priority = priority;
                            m_entsqueue.Enqueue(item);
                            }
                        }
                    entities = null;
                    // send them 
                    SendQueued(m_entsqueue);
                    }
                }

            //Add the time to the stats tracker
            IAgentUpdateMonitor reporter = (IAgentUpdateMonitor)m_presence.Scene.RequestModuleInterface<IMonitorModule>().GetMonitor(m_presence.Scene.RegionInfo.RegionID.ToString(), "Agent Update Count");
            if (reporter != null)
                reporter.AddAgentTime(Util.EnvironmentTickCountSubtract(AgentMS));

            m_inUse = false;
            }

        private void SendQueued(PriorityQueue<EntityUpdate, double> m_entsqueue)
            {
            PriorityQueueItem<EntityUpdate, double> up;
            
            while (m_entsqueue.TryDequeue(out up))
                {
                //Make sure not send deleted or null objects

                SceneObjectGroup ent = (SceneObjectGroup)up.Value.Entity;
                if (ent == null || ent.IsDeleted)
                    continue;

                if (ent.RootPart.Shape.PCode == 9 && ent.RootPart.Shape.State != 0)
                    {
                    byte state = ent.RootPart.Shape.State;
                    if ((
                            state == (byte)AttachmentPoint.HUDBottom ||
                            state == (byte)AttachmentPoint.HUDBottomLeft ||
                            state == (byte)AttachmentPoint.HUDBottomRight ||
                            state == (byte)AttachmentPoint.HUDCenter ||
                            state == (byte)AttachmentPoint.HUDCenter2 ||
                            state == (byte)AttachmentPoint.HUDTop ||
                            state == (byte)AttachmentPoint.HUDTopLeft ||
                            state == (byte)AttachmentPoint.HUDTopRight
                            )
                            &&
                            ent.RootPart.OwnerID != m_presence.UUID)
                        continue;
                    }
                SendUpdate(up.Value.Flags, ent);
                 }
            m_entsqueue.Clear();

            // send things that arrived meanwhile
            lock (m_delayedUpdates)
                {
                object o;
                while (m_delayedUpdates.Count > 0)
                    {
                    o = m_delayedUpdates.Dequeue();
                    if (o == null)
                        break;
                    SceneObjectPart p = (SceneObjectPart)((object[])o)[0];
                    PrimUpdateFlags updateFlags = (PrimUpdateFlags)((object[])o)[1];
                    SendUpdate(p, m_presence.GenerateClientFlags(p), updateFlags);
                    }
                m_lastUpdatePos = (m_presence.IsChildAgent) ?
                    m_presence.AbsolutePosition :
                    m_presence.CameraPosition;
                lastDrawDistance = m_presence.DrawDistance;
                if (lastDrawDistance < 32)
                    lastDrawDistance = 32;
                m_queueing = false;
                }
            }


            #endregion
/*
            #region Update loop that sends objects that have been recently added to the queue

            //Pull the parts out into a list first so that we don't lock the queue for too long
//            Dictionary<UUID, List<EntityUpdate>> m_parentUpdates = new Dictionary<UUID, List<EntityUpdate>>();
            //            lock (m_partsUpdateQueue)
                        {
            //                lock (m_removeNextUpdateOf)
                            {
                                PriorityQueueItem<EntityUpdate, double> update;
                                bool cont=true;
                                while (cont)                       
                                    {
                                    lock (m_partsUpdateQueue)
                                        {
                                        cont = m_partsUpdateQueue.TryDequeue(out update);
                                        }
                                    if (!cont)
                                        break;

                                    if (update.Value == null)
                                        continue;
                                    //Make sure not to send deleted or null objects
                                    SceneObjectGroup ent;
                                    if (update.Value.Entity is SceneObjectPart)
                                        ent = ((SceneObjectPart)update.Value.Entity).ParentGroup;
                                    else if (update.Value.Entity is SceneObjectGroup)
                                        ent = (SceneObjectGroup)update.Value.Entity;
                                    else
                                        continue;
                        
                                    if (ent == null || ent.IsDeleted)
                                            continue;
                                    //Make sure we are not supposed to remove it

                                    if (m_removeNextUpdateOf.ContainsKey(ent.LocalId))
                                    {
                                    if (update.Value.Version > m_removeNextUpdateOf[ent.LocalId])
                                        {
                                            //This update is newer, let it go on by
                                        }
                                        else //Not newer, should be removed Note: if this is the same version, its the update we were supposed to remove... so we do NOT do >= above
                                            continue;
                                    }

                                    //Make sure we are not supposed to remove it
                                    if (m_removeUpdateOf.Contains(ent.LocalId))
                                        continue;

                                    if (!m_parentUpdates.ContainsKey(ent.UUID))
                                        m_parentUpdates.Add(ent.UUID, new List<EntityUpdate>());

                                    m_parentUpdates[ent.UUID].Add(update.Value);

                                    List<SceneObjectPart> parts = ent.ChildrenList;
                                    foreach (SceneObjectPart part in parts)
                                        {
                                        if (!CheckForCulling(part.ParentGroup))
                                            continue;

                                        // Attachment handling. Attachments are 'special' and we have to send the full group update when we send updates
                                        if (part.ParentGroup.RootPart.Shape.PCode == 9 && part.ParentGroup.RootPart.Shape.State != 0)
                                            {
                                            if (part != part.ParentGroup.RootPart)
                                                continue;

                                            //Check to make sure this attachment is not a hud. Attachments that are huds are 
                                            //   ONLY sent to the owner, noone else!
                                            if (
                                                (
                                                part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDBottom ||
                                                part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDBottomLeft ||
                                                part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDBottomRight ||
                                                part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDCenter ||
                                                part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDCenter2 ||
                                                part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDTop ||
                                                part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDTopLeft ||
                                                part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDTopRight
                                                )
                                                &&
                                                part.OwnerID != m_presence.UUID)
                                                continue;

                                            SendUpdate(update.Value.Flags, part.ParentGroup);
                                            continue;
                                            }
                                        SendUpdate(part,
                                        m_presence.GenerateClientFlags(part), update.Value.Flags);
                                        }
                                m_removeNextUpdateOf.Clear();
                            }
                        }

                        //Now loop through the list and send the updates
                        foreach (UUID ParentID in m_parentUpdates.Keys)
                        {
                            //Sort by LinkID
            //                m_parentUpdates[ParentID].Sort(linkSetSorter);
                            foreach (EntityUpdate update in m_parentUpdates[ParentID])
                            {
                                SceneObjectPart part = ((SceneObjectGroup)update.Entity).RootPart;
                                //Check for culling here!
                                if (!CheckForCulling(part.ParentGroup))
                                    continue;

                                // Attachment handling. Attachments are 'special' and we have to send the full group update when we send updates
                                if (part.ParentGroup.RootPart.Shape.PCode == 9 && part.ParentGroup.RootPart.Shape.State != 0)
                                {
                                    if (part != part.ParentGroup.RootPart)
                                        continue;

                                    //Check to make sure this attachment is not a hud. Attachments that are huds are 
                                    //   ONLY sent to the owner, noone else!
                                    if (
                                        (
                                        part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDBottom ||
                                        part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDBottomLeft ||
                                        part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDBottomRight ||
                                        part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDCenter ||
                                        part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDCenter2 ||
                                        part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDTop ||
                                        part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDTopLeft ||
                                        part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDTopRight
                                        )
                                        && 
                                        part.OwnerID != m_presence.UUID)
                                        continue;

                                    SendUpdate(update.Flags, part.ParentGroup);
                                    continue;
                                }

                                SendUpdate(part,
                                        m_presence.GenerateClientFlags(part), update.Flags);
                            }

                        }
           
            #endregion

            //Add the time to the stats tracker
            IAgentUpdateMonitor reporter = (IAgentUpdateMonitor)m_presence.Scene.RequestModuleInterface<IMonitorModule>().GetMonitor(m_presence.Scene.RegionInfo.RegionID.ToString(), "Agent Update Count");
            if (reporter != null)
                reporter.AddAgentTime(Util.EnvironmentTickCountSubtract(AgentMS));

            m_inUse = false;
            }
*/
        /// <summary>
        /// Sorts a list of Parts by Link Number so they end up in the correct order
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public int linkSetSorter(EntityUpdate a, EntityUpdate b)
        {
            return a.Entity.LinkNum.CompareTo(b.Entity.LinkNum);
        }

        #endregion

        #region Send the packets to the client handler

        /// <summary>
        /// Send a full update to the client for the given part
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="clientFlags"></param>
        protected internal void SendUpdate(SceneObjectPart part, uint clientFlags, PrimUpdateFlags changedFlags)
        {
            Vector3 lPos;
            if (part.IsRoot)
            {
                if (part.IsAttachment)
                {
                    lPos = part.AttachedPos;
                }
                else
                {
                    lPos = part.AbsolutePosition;
                }
            }
            else
            {
                lPos = part.OffsetPosition;
            }

            // Suppress full updates during attachment editing
            if (part.ParentGroup.IsSelected && part.IsAttachment)
                return;

            clientFlags &= ~(uint)PrimFlags.CreateSelected;

            if (m_presence.UUID == part.OwnerID)
            {
                if ((part.Flags & PrimFlags.CreateSelected) != 0)
                {
                    clientFlags |= (uint)PrimFlags.CreateSelected;
                    part.Flags &= ~PrimFlags.CreateSelected;
                }
            }
            m_presence.ControllingClient.SendPrimUpdate(part, changedFlags);
        }

        /// <summary>
        /// This sends updates for all the prims in the group
        /// </summary>
        /// <param name="UpdateFlags"></param>
        /// <param name="grp"></param>
        protected internal void SendUpdate(PrimUpdateFlags UpdateFlags, SceneObjectGroup grp)
        {
            SendUpdate(
                grp.RootPart, m_presence.Scene.Permissions.GenerateClientFlags(m_presence.UUID, grp.RootPart), UpdateFlags);

            List<SceneObjectPart> children;
            lock (grp.ChildrenList)
            {
                children = new List<SceneObjectPart>(grp.ChildrenList);
            }
            foreach (SceneObjectPart part in children)
            {
                if (part != grp.RootPart)
                    SendUpdate(
                        part, m_presence.Scene.Permissions.GenerateClientFlags(m_presence.UUID, part), UpdateFlags);
            }
        }

        #endregion

        #region Reset and Close

        /// <summary>
        /// Reset all lists that have to deal with what updates the viewer has
        /// </summary>
        public void Reset()
        {
            /*
            m_inUse = false;
            m_updatesNeedReprioritization = false;
            m_SentInitialObjects = false;
            m_removeNextUpdateOf.Clear();
            m_removeUpdateOf.Clear();
            m_objectsInView.Clear();
             */
        }

        #endregion
    }
#if testViewer
    public class TestSceneViewer
    {
         #region Declares

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        
        protected ScenePresence m_presence;
        protected PriorityQueue<EntityUpdate, double> m_partsUpdateQueue;
        /// <summary>
        /// Param 1 - LocalID of the object, Param 2 - The last version when this was added to the list
        /// </summary>
        protected Dictionary<uint, int> m_removeNextUpdateOf = new Dictionary<uint, int>();
        protected List<uint> m_removeUpdateOf = new List<uint>();
        protected bool m_SentInitialObjects = false;
        protected volatile bool m_inUse = false;
        protected volatile Dictionary<UUID /* UserID*/, List<UUID> /* PartID*/> m_objectsInView = new Dictionary<UUID, List<UUID>>();
        protected Prioritizer m_prioritizer;
        private readonly Mutex _versionAllocateMutex = new Mutex(false);
        protected int m_lastVersion = 0;
        //private Scene m_scene;

        public Prioritizer Prioritizer
        {
            get { return m_prioritizer; }
        }

        #endregion

        #region Constructor

        public TestSceneViewer()
        {
            //m_prioritizer = new Prioritizer(presence.Scene);
            //m_partsUpdateQueue = new PriorityQueue<EntityUpdate, double>(presence.Scene.Entities.Count > 1000 ? presence.Scene.Entities.Count : 1000);
        }

        #endregion

        #region Enqueue/Remove updates for objects

        /// <summary>
        /// Add the objects to the queue for which we need to send an update to the client
        /// </summary>
        /// <param name="part"></param>
        public void QueuePartForUpdate(SceneObjectPart part, PrimUpdateFlags UpdateFlags)
        {
            EntityUpdate update = new EntityUpdate(part, UpdateFlags);
            //Fix the version with the newest locked m_version
            FixVersion(update);
            /*foreach(ScenePresence client in m_scene.ScenePresences)
            {
                //Check for culling here, as it is per client!
                if (!CheckForCulling(client, part.ParentGroup))
                     continue;
                double priority = m_prioritizer.GetUpdatePriority(client.ControllingClient, part.ParentGroup);
                PriorityQueueItem<EntityUpdate, double> item = new PriorityQueueItem<EntityUpdate, double>();
                item.Priority = priority;
                item.Value = update;
                m_partsUpdateQueue.Enqueue(item);

                //Remove it so that we send the update later
                RemoveObjectInViewForClient(part.UUID);
            }*/
        }

        /// <summary>
        /// Updates the last version securely and set the update's version correctly
        /// </summary>
        /// <param name="update"></param>
        private void FixVersion(EntityUpdate update)
        {
            _versionAllocateMutex.WaitOne();
            m_lastVersion++;
            update.Version = m_lastVersion;
            _versionAllocateMutex.ReleaseMutex();
        }

        /// <summary>
        /// Get the current version securely as we lock the mutex for it
        /// </summary>
        /// <returns></returns>
        private int GetVersion()
        {
            int version;

            _versionAllocateMutex.WaitOne();
            version = m_lastVersion;
            _versionAllocateMutex.ReleaseMutex();

            return version;
        }

        /// <summary>
        /// Clear the updates for this part in the next update loop
        /// </summary>
        /// <param name="part"></param>
        public void ClearUpdatesForPart(SceneObjectPart part)
        {
            lock (m_removeUpdateOf)
            {
                //Add it to the list to check and make sure that we do not send updates for this object
                m_removeUpdateOf.Add(part.LocalId);
            }
                //Make it check when the user comes around to it again
                     RemoveObjectInViewForClient(part.UUID);
        }

       private void RemoveObjectInViewForClient(UUID partID)
       {
            /*lock(m_objectsInView)
            {
                  if(m_objectsInView.ContainsKey(userID))
                       m_objectsInView[userID].Remove(partID);
            }*/
       }

        /// <summary>
        /// Clear the updates for this part in the next update loop
        /// </summary>
        /// <param name="part"></param>
        public void ClearUpdatesForOneLoopForPart(SceneObjectPart part)
        {
            lock (m_removeNextUpdateOf)
            {
                //Add it to the list to check and make sure that we do not send updates for this object
                m_removeNextUpdateOf.Add(part.LocalId, GetVersion());
            }
                //Make it check when the user comes around to it again
                     RemoveObjectInViewForClient(part.UUID);
        }

        #endregion

        #region Object Culling by draw distance

        /// <summary>
        /// When the client moves enough to trigger this, make sure that we have sent
        ///  the client all of the objects that have just entered their FOV in their draw distance.
        /// </summary>
        /// <param name="remote_client"></param>
        private void SignificantClientMovement(ScenePresence client)
        {
            if (!client.Scene.CheckForObjectCulling)
                return;

            //If the draw distance is 0, the client has gotten messed up or something and we can't do this...
            if(client.DrawDistance == 0)
                return;

            //This checks to see if we need to send more updates to the avatar since they last moved
            ISceneEntity[] Entities = client.Scene.Entities.GetEntities ();

            foreach (ISceneEntity entity in Entities)
            {
                if (entity is SceneObjectGroup) //Only objects
                {
                    //Check to see if they are in range
                    if (CheckForCulling(client, entity))
                    {
                        //Check if we already have sent them an update
                        /*if (!m_objectsInView.Contains(entity.UUID))
                        {
                            //Update the list
                            m_objectsInView.Add(entity.UUID);
                            //Send the update
                            SendUpdate(PrimUpdateFlags.FullUpdate, (SceneObjectGroup)entity); //New object, send full
                        }*/
                    }
                }
            }
            Entities = null;
        }

        /// <summary>
        /// Checks to see whether the object should be sent to the client
        /// Returns true if the client should be able to see the object, false if not
        /// </summary>
        /// <param name="grp"></param>
        /// <returns></returns>
        private bool CheckForCulling(ScenePresence presence, ISceneEntity grp)
        {
            if (!presence.Scene.CheckForObjectCulling)
                return true;
            if (presence.DrawDistance > 0)
            {
                Vector3 gprpos = grp.AbsolutePosition; // not doing this was making several calls
                Vector3 grphsize = grp.GroupScale(); // and this is heavy someone needs to look to it and cache for static prims
                Quaternion grprot = grp.GroupRotation;

                grphsize = grphsize * grprot; // prims are rotated in world
                grphsize = grphsize * 0.5f; // save same cpu in the 2 calls
                //Check for part position against the av and the camera position
                if (CheckCullingAgainstPosition(presence.AbsolutePosition, gprpos, grphsize, presence.DrawDistance) ||
                    CheckCullingAgainstPosition(presence.CameraPosition, gprpos, grphsize, presence.DrawDistance))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks to see whether the client should be able to see the given object from the given position
        /// </summary>
        /// <param name="checkPosition"></param>
        /// <param name="groupPosition"></param>
        /// <param name="groupSize"></param>
        /// <returns></returns>
        private bool CheckCullingAgainstPosition(Vector3 checkPosition, Vector3 groupPosition, Vector3 groupHalfSizeRotated, float DrawDistance)
        {
            //First just check against the position 
            // not good prims can be huge and also distance calls sqrt and that takes more time than all the rest of this code
//            if (Util.DistanceLessThan(checkPosition, groupPosition, DrawDistance))
//                return true;
            //Next, start checking aginst the group corners
            //NOTE: This really should be checking as a sphere.. but that hasn't been done yet

            // no check grp AABB against a distance cube


            if (Math.Abs(checkPosition.X - groupPosition.X) - groupHalfSizeRotated.X < DrawDistance)
                return true;
            if (Math.Abs(checkPosition.Y - groupPosition.Y) - groupHalfSizeRotated.Y < DrawDistance)
                return true;
            if (Math.Abs(checkPosition.Z - groupPosition.Z) - groupHalfSizeRotated.Z < DrawDistance)
                return true;
            //All done then...
            return false;
        }

        #endregion

        #region SendPrimUpdates

        /// <summary>
        /// This loops through all of the lists that we have for the client
        ///  as well as checking whether the client has ever entered the sim before
        ///  and sending the needed updates to them if they have just entered.
        ///  NOTE: This does add the updates to the LLUDPClient queue, it does NOT have prioritization built in before this method!
        /// </summary>
        public void SendPrimUpdates(IScenePresence client)
        {
            if (m_inUse)
                return;
            m_inUse = true;
            //This is for stats
            int AgentMS = Util.EnvironmentTickCount();

            #region New client entering the Scene, requires all objects in the Scene

            ///If we havn't started processing this client yet, we need to send them ALL the prims that we have in this Scene (and deal with culling as well...)
            if (!m_SentInitialObjects)
            {
                m_SentInitialObjects = true;
                //If they are not in this region, we check to make sure that we allow seeing into neighbors
                /*if (!client.IsChildAgent || (client.Scene.RegionInfo.SeeIntoThisSimFromNeighbor))
                {
                    EntityBase[] entities = m_scene.Entities.GetEntities();
                    //Use the PriorityQueue so that we can send them in the correct order
                    
                    foreach (EntityBase e in entities)
                    {
                        if (e != null && e is SceneObjectGroup)
                        {
                            //Check for culling here!
                            if (!CheckForCulling(client, e))
                                continue;

                            //Get the correct priority and add to the queue
                            SendUpdate(PrimUpdateFlags.FullUpdate, (SceneObjectGroup)e);
                        }
                    }
                    entities = null;
                }*/
            }

            #endregion

            #region Update loop that sends objects that have been recently added to the queue

            //Pull the parts out into a list first so that we don't lock the queue for too long
            Dictionary<UUID, List<EntityUpdate>> m_parentUpdates = new Dictionary<UUID, List<EntityUpdate>>();
            lock (m_partsUpdateQueue)
            {
                lock (m_removeNextUpdateOf)
                {
                    PriorityQueueItem<EntityUpdate, double> update;
                    while (m_partsUpdateQueue.TryDequeue(out update))
                    {
                        if (update.Value == null)
                            continue;
                        //Make sure not to send deleted or null objects
                        if (((SceneObjectPart)update.Value.Entity).ParentGroup == null || ((SceneObjectPart)update.Value.Entity).ParentGroup.IsDeleted)
                            continue;

                        //Make sure we are not supposed to remove it
                        if (m_removeNextUpdateOf.ContainsKey(update.Value.Entity.LocalId))
                        {
                            if (update.Value.Version > m_removeNextUpdateOf[update.Value.Entity.LocalId])
                            {
                                //This update is newer, let it go on by
                            }
                            else //Not newer, should be removed Note: if this is the same version, its the update we were supposed to remove... so we do NOT do >= above
                                continue;
                        }

                        //Make sure we are not supposed to remove it
                        if (m_removeUpdateOf.Contains(update.Value.Entity.LocalId))
                            continue;

                        if (!m_parentUpdates.ContainsKey(((SceneObjectPart)update.Value.Entity).ParentGroup.UUID))
                            m_parentUpdates.Add(((SceneObjectPart)update.Value.Entity).ParentGroup.UUID, new List<EntityUpdate>());

                        m_parentUpdates[((SceneObjectPart)update.Value.Entity).ParentGroup.UUID].Add(update.Value);
                    }
                    m_removeNextUpdateOf.Clear();
                }
            }

            //Now loop through the list and send the updates
            foreach (UUID ParentID in m_parentUpdates.Keys)
            {
                //Sort by LinkID
                m_parentUpdates[ParentID].Sort(linkSetSorter);
                foreach (EntityUpdate update in m_parentUpdates[ParentID])
                {
                    SceneObjectPart part = ((SceneObjectPart)update.Entity);

                    // Attachment handling. Attachments are 'special' and we have to send the full group update when we send updates
                    if (part.ParentGroup.RootPart.Shape.PCode == 9 && part.ParentGroup.RootPart.Shape.State != 0)
                    {
                        if (part != part.ParentGroup.RootPart)
                            continue;

                        //Check to make sure this attachment is not a hud. Attachments that are huds are 
                        //   ONLY sent to the owner, noone else!
                        if (
                            (part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDBottom ||
                            part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDBottomLeft ||
                            part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDBottomRight ||
                            part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDCenter ||
                            part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDCenter2 ||
                            part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDTop ||
                            part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDTopLeft ||
                            part.ParentGroup.RootPart.Shape.State == (byte)AttachmentPoint.HUDTopRight)
                            && 
                            part.OwnerID != m_presence.UUID)
                            continue;

                        SendUpdate(update.Flags, part.ParentGroup);
                        continue;
                    }

                    SendUpdate(part,
                            m_presence.GenerateClientFlags(part), update.Flags);
                }
            }

            #endregion

            //Add the time to the stats tracker
            IAgentUpdateMonitor reporter = (IAgentUpdateMonitor)m_presence.Scene.RequestModuleInterface<IMonitorModule>().GetMonitor(m_presence.Scene.RegionInfo.RegionID.ToString(), "Agent Update Count");
            if (reporter != null)
                reporter.AddAgentTime(Util.EnvironmentTickCountSubtract(AgentMS));

            m_inUse = false;
        }
        
        /// <summary>
        /// Sorts a list of Parts by Link Number so they end up in the correct order
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public int linkSetSorter(EntityUpdate a, EntityUpdate b)
        {
            return a.Entity.LinkNum.CompareTo(b.Entity.LinkNum);
        }

        #endregion

        #region Send the packets to the client handler

        /// <summary>
        /// Send a full update to the client for the given part
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="clientFlags"></param>
        protected internal void SendUpdate(SceneObjectPart part, uint clientFlags, PrimUpdateFlags changedFlags)
        {
            Vector3 lPos;
            if (part.IsRoot)
            {
                if (part.IsAttachment)
                {
                    lPos = part.AttachedPos;
                }
                else
                {
                    lPos = part.AbsolutePosition;
                }
            }
            else
            {
                lPos = part.OffsetPosition;
            }

            // Suppress full updates during attachment editing
            if (part.ParentGroup.IsSelected && part.IsAttachment)
                return;

            clientFlags &= ~(uint)PrimFlags.CreateSelected;

            if (m_presence.UUID == part.OwnerID)
            {
                if ((part.Flags & PrimFlags.CreateSelected) != 0)
                {
                    clientFlags |= (uint)PrimFlags.CreateSelected;
                    part.Flags &= ~PrimFlags.CreateSelected;
                }
            }
            m_presence.ControllingClient.SendPrimUpdate(part, changedFlags);
        }

        /// <summary>
        /// This sends updates for all the prims in the group
        /// </summary>
        /// <param name="UpdateFlags"></param>
        /// <param name="grp"></param>
        protected internal void SendUpdate(PrimUpdateFlags UpdateFlags, SceneObjectGroup grp)
        {
            SendUpdate(
                grp.RootPart, m_presence.Scene.Permissions.GenerateClientFlags(m_presence.UUID, grp.RootPart), UpdateFlags);

            List<SceneObjectPart> children;
            lock (grp.ChildrenList)
            {
                children = new List<SceneObjectPart>(grp.ChildrenList);
            }
            foreach (SceneObjectPart part in children)
            {
                if (part != grp.RootPart)
                    SendUpdate(
                        part, m_presence.Scene.Permissions.GenerateClientFlags(m_presence.UUID, part), UpdateFlags);
            }
        }

        #endregion

        #region Reset and Close

        /// <summary>
        /// Reset all lists that have to deal with what updates the viewer has
        /// </summary>
        public void Reset()
        {
            m_inUse = false;
            m_SentInitialObjects = false;
            m_removeNextUpdateOf.Clear();
            m_removeUpdateOf.Clear();
            m_objectsInView.Clear();
        }

        #endregion
    }
#endif
}
