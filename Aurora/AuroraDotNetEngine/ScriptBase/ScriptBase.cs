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
using System.Runtime.Remoting;
using System.Runtime.Remoting.Lifetime;
using System.Security.Permissions;
using System.Threading;
using System.Reflection;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using log4net;
using OpenSim.Framework;
using Aurora.ScriptEngine.AuroraDotNetEngine;
using Aurora.ScriptEngine.AuroraDotNetEngine.APIs.Interfaces;
using Aurora.ScriptEngine.AuroraDotNetEngine.CompilerTools;

namespace Aurora.ScriptEngine.AuroraDotNetEngine.Runtime
{
    [Serializable]
    public partial class ScriptBaseClass : MarshalByRefObject, IScript, IDisposable
    {
        private ScriptSponsor m_sponser;

        public ISponsor Sponsor
        {
            get
            {
                return m_sponser;
            }
        }

        private bool m_stateSaveRequired = false;
        public bool NeedsStateSaved
        {
            get
            {
                if (m_stateSaveRequired)
                    return true;
                if (!m_useStateSaves)
                    return false;
                if (m_lastStateSaveValues == null)
                    m_lastStateSaveValues = m_InitialValues;
                Dictionary<string, object> vars = GetVars ();
                foreach (KeyValuePair<string, object> kvp in vars)
                {
                    if (m_lastStateSaveValues[kvp.Key].ToString() != kvp.Value.ToString()) //Something changed!
                        return true;
                }
                return false;
            }
            set
            {
                m_stateSaveRequired = value;
                if (!m_useStateSaves)
                    return;
                //Besides setting the value, if we don't need one, save the vars we have for the last state save as well
                if(!value)
                    m_lastStateSaveValues = GetVars ();
            }
        }

        private Dictionary<string, object> m_lastStateSaveValues = null;
        public IScene Scene = null;
        public ISceneChildEntity Object = null;
        public bool m_useStateSaves = true;

        public void SetSceneRefs (IScene scene, ISceneChildEntity child, bool useStateSaves)
        {
            Scene = scene;
            Object = child;
            m_useStateSaves = useStateSaves;
        }

        public override Object InitializeLifetimeService()
        {
            try
            {
                ILease lease = (ILease)base.InitializeLifetimeService();

                if (lease.CurrentState == LeaseState.Initial)
                {
                    // Infinite : lease.InitialLeaseTime = TimeSpan.FromMinutes(0);
                    lease.InitialLeaseTime = TimeSpan.FromMinutes(0);
                    //lease.InitialLeaseTime = TimeSpan.FromMinutes(0);
                    //lease.RenewOnCallTime = TimeSpan.FromMinutes(10.0);
                    //lease.SponsorshipTimeout = TimeSpan.FromMinutes(1.0);
                }
                return lease;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void UpdateLease(TimeSpan time)
        {
            ILease lease = (ILease)RemotingServices.GetLifetimeService(this as MarshalByRefObject);
            if (lease != null)
                lease.Renew(time);
        }

        #if DEBUG
        // For tracing GC while debugging
        public static bool GCDummy = false;
        ~ScriptBaseClass()
        {
            GCDummy = true;
        }
        #endif

        public ScriptBaseClass()
        {
            m_Executor = new Executor(this);

            m_sponser = new ScriptSponsor();
        }

        public Executor m_Executor = null;

        public virtual long GetStateEventFlags(string state)
        {
            return (long)m_Executor.GetStateEventFlags(state);
        }

        public EnumeratorInfo ExecuteEvent(string state, string FunctionName, object[] args, EnumeratorInfo Start, out Exception ex)
        {
            return m_Executor.ExecuteEvent(state, FunctionName, args, Start, out ex);
        }

        public bool CheckSlice()
        {
            return m_Executor.CheckSlice ();
        }

        private Dictionary<string, object> m_InitialValues =
                new Dictionary<string, object>();
        private Dictionary<string, FieldInfo> m_Fields =
                new Dictionary<string, FieldInfo>();
        public Dictionary<string, IScriptApi> m_apis = new Dictionary<string, IScriptApi>();

        public void InitApi(IScriptApi data)
        {
            /*ILease lease = (ILease)RemotingServices.GetLifetimeService(data as MarshalByRefObject);
            if (lease != null)
                lease.Register(m_sponser);*/
            m_apis.Add(data.Name, data);
        }

        public void UpdateInitialValues()
        {
            m_InitialValues = GetVars();
        }

        public virtual void StateChange(string newState)
        {
        }

        public void Close()
        {
            m_sponser.Close();
        }

        public Dictionary<string, object> GetVars()
        {
            Dictionary<string, object> vars = new Dictionary<string, object>();

            if (m_Fields == null)
                return vars;

            m_Fields.Clear();

            Type t = GetType();

            FieldInfo[] fields = t.GetFields(BindingFlags.NonPublic |
                                             BindingFlags.Public |
                                             BindingFlags.Instance |
                                             BindingFlags.DeclaredOnly);

            foreach (FieldInfo field in fields)
            {
                m_Fields[field.Name] = field;

                if (field.FieldType == typeof(LSL_Types.list)) // ref type, copy
                {
                    LSL_Types.list v = (LSL_Types.list)field.GetValue(this);
                    if (((object)v) == null)
                        continue;//Broken... :/
                    Object[] data = new Object[v.Data.Length];
                    Array.Copy(v.Data, 0, data, 0, v.Data.Length);
                    LSL_Types.list c = new LSL_Types.list();
                    c.Data = data;
                    vars[field.Name] = c;
                }
                else if (field.FieldType == typeof(LSL_Types.LSLInteger) ||
                        field.FieldType == typeof(LSL_Types.LSLString) ||
                        field.FieldType == typeof(LSL_Types.LSLFloat) ||
                        field.FieldType == typeof(Int32) ||
                        field.FieldType == typeof(Double) ||
                        field.FieldType == typeof(Single) ||
                        field.FieldType == typeof(String) ||
                        field.FieldType == typeof(Byte) ||
                        field.FieldType == typeof(short) ||
                        field.FieldType == typeof(LSL_Types.Vector3) ||
                        field.FieldType == typeof(LSL_Types.Quaternion))
                {
                    vars[field.Name] = field.GetValue(this);
                }
            }

            return vars;
        }

        /// <summary>
        /// Note: this is just used for reset
        /// </summary>
        /// <param name="vars"></param>
        public void SetVars(Dictionary<string, object> vars)
        {
            if (!m_useStateSaves)
                return;
            foreach (KeyValuePair<string, object> var in vars)
            {
                if (m_Fields.ContainsKey (var.Key))
                {
                    if (m_Fields[var.Key].FieldType == typeof (LSL_Types.list))
                    {
                        LSL_Types.list v = (LSL_Types.list)m_Fields[var.Key].GetValue (this);
                        Object[] data = ((LSL_Types.list)(var.Value)).Data;
                        v.Data = new Object[data.Length];
                        Array.Copy (data, 0, v.Data, 0, data.Length);
                        m_Fields[var.Key].SetValue (this, v);
                    }

                    else if (m_Fields[var.Key].FieldType == typeof (LSL_Types.LSLInteger) ||
                            m_Fields[var.Key].FieldType == typeof (LSL_Types.LSLString) ||
                            m_Fields[var.Key].FieldType == typeof (LSL_Types.LSLFloat) ||
                            m_Fields[var.Key].FieldType == typeof (Int32) ||
                            m_Fields[var.Key].FieldType == typeof (Double) ||
                            m_Fields[var.Key].FieldType == typeof (Single) ||
                            m_Fields[var.Key].FieldType == typeof (String) ||
                            m_Fields[var.Key].FieldType == typeof (Byte) ||
                            m_Fields[var.Key].FieldType == typeof (short) ||
                            m_Fields[var.Key].FieldType == typeof (LSL_Types.Vector3) ||
                            m_Fields[var.Key].FieldType == typeof (LSL_Types.Quaternion)
                        )
                    {
                        m_Fields[var.Key].SetValue (this, var.Value);
                    }
                }
            }
        }

        public string GetShortType(object o)
            {
            string tmp = o.GetType().ToString();
            int i = tmp.LastIndexOf('+');
            string type = tmp.Substring(i+1);
            return type;
            }

        public string ListToString(object o)
            {
            string tmp = "";
            string cur = "";
            LSL_Types.list v = (LSL_Types.list)o;
            foreach (object ob in v.Data)
                {
                if (ob.GetType() == typeof(LSL_Types.LSLInteger))
                    cur = "i" + ob.ToString();
                else if (ob.GetType() == typeof(LSL_Types.LSLFloat))
                    cur = "f" + ob.ToString();
                else if (ob.GetType() == typeof(LSL_Types.Vector3))
                    cur = "v" + ob.ToString();
                else if (ob.GetType() == typeof(LSL_Types.Quaternion))
                    cur = "q" + ob.ToString();
                else if (ob.GetType() == typeof(LSL_Types.LSLString))
                    cur = "\"" + ob.ToString() + "\"";
                else if (ob.GetType() == typeof(LSL_Types.key))
                    cur = "k\"" + ob.ToString() + "\"";
                else if (o.GetType() == typeof(LSL_Types.list))
                    cur = "{" + ListToString(ob) + "}";

                if (tmp == "")
                    tmp = cur;
                else
                    tmp += ", " + cur;
                }
            return tmp;
            }

        public Dictionary<string, object> GetStoreVars ()
        {
            Dictionary<string, object> vars = new Dictionary<string, object> ();
            if (!m_useStateSaves)
                return vars;

            if (m_Fields == null)
                return vars;

            m_Fields.Clear ();

            Type t = GetType ();

            FieldInfo[] fields = t.GetFields (BindingFlags.NonPublic |
                                             BindingFlags.Public |
                                             BindingFlags.Instance |
                                             BindingFlags.DeclaredOnly);

            foreach (FieldInfo field in fields)
            {
                m_Fields[field.Name] = field;

                if (field.FieldType == typeof (LSL_Types.list)) // ref type, copy
                {
                    string tmp = "";
                    string cur = "";
                    LSL_Types.list v = (LSL_Types.list)field.GetValue (this);
                    foreach (object o in v.Data)
                    {
                        if (o.GetType () == typeof (LSL_Types.LSLInteger))
                            cur = "i" + o.ToString ();
                        else if (o.GetType () == typeof (LSL_Types.LSLFloat))
                            cur = "f" + o.ToString ();
                        else if (o.GetType () == typeof (LSL_Types.Vector3))
                            cur = "v" + o.ToString ();
                        else if (o.GetType () == typeof (LSL_Types.Quaternion))
                            cur = "q" + o.ToString ();
                        else if (o.GetType () == typeof (LSL_Types.LSLString))
                            cur = "\"" + o.ToString () + "\"";
                        else if (o.GetType () == typeof (LSL_Types.key))
                            cur = "k\"" + o.ToString () + "\"";
                        else if (o.GetType () == typeof (LSL_Types.list))
                            cur = "{" + ListToString (o) + "}";

                        if (tmp == "")
                            tmp = cur;
                        else
                            tmp += ", " + cur;
                    }
                    vars[field.Name] = (Object)tmp;
                }
                else if (field.FieldType == typeof (LSL_Types.LSLInteger) ||
                        field.FieldType == typeof (LSL_Types.LSLString) ||
                        field.FieldType == typeof (LSL_Types.LSLFloat) ||
                        field.FieldType == typeof (Int32) ||
                        field.FieldType == typeof (Double) ||
                        field.FieldType == typeof (Single) ||
                        field.FieldType == typeof (String) ||
                        field.FieldType == typeof (Byte) ||
                        field.FieldType == typeof (short) ||
                        field.FieldType == typeof (LSL_Types.Vector3) ||
                        field.FieldType == typeof (LSL_Types.Quaternion))
                {
                    vars[field.Name] = field.GetValue (this).ToString ();
                }
            }

            return vars;
        }

        public LSL_Types.list ParseValueToList(string inval, int start, out int end)
        {
            LSL_Types.list v = new LSL_Types.list();
            end = -1;
            char c;
            string tr = ",}";
            char[] charany = tr.ToCharArray();
            string param = "";
            int totlen = inval.Length;
            int len;

            while (true)
            {
                try
                {
                    if (inval.Length == 0)
                        v.Add(new LSL_Types.LSLString(""));
                    else
                    {
                        c = inval[start++];
                        switch (c)
                        {
                            case 'i':
                                end = inval.IndexOfAny(charany, start);
                                if (end > 0)
                                    len = end - start;
                                else
                                    len = totlen - start;
                                param = inval.Substring(start, len);
                                v.Add(new LSL_Types.LSLInteger(param));
                                break;
                            case 'f':
                                end = inval.IndexOfAny(charany, start);
                                if (end > 0)
                                    len = end - start;
                                else
                                    len = totlen - start;
                                param = inval.Substring(start, len);
                                v.Add(new LSL_Types.LSLFloat(param));
                                break;
                            case 'v':
                                end = inval.IndexOf('>', start);
                                if (end > 0)
                                    len = end - start;
                                else
                                    len = totlen - start;
                                param = inval.Substring(start, len);
                                v.Add(new LSL_Types.Vector3(param));
                                end++;
                                break;
                            case 'q':
                                end = inval.IndexOf('>', start);
                                if (end > 0)
                                    len = end - start;
                                else
                                    len = totlen - start;
                                param = inval.Substring(start, len);
                                v.Add(new LSL_Types.Quaternion(param));
                                end++;
                                break;
                            case '"':
                                end = inval.IndexOf('"', start);
                                if (end > 0)
                                    len = end - start;
                                else
                                    len = totlen - start;
                                param = inval.Substring(start, len);
                                v.Add(new LSL_Types.LSLString(param));
                                end++;
                                break;
                            case 'k':
                                start++;
                                end = inval.IndexOf('"', start);
                                if (end > 0)
                                    len = end - start;
                                else
                                    len = totlen - start;
                                param = inval.Substring(start, len);
                                v.Add(new LSL_Types.key(param));
                                end++;
                                break;
                            case '{':
                                v.Add(ParseValueToList(inval, start, out end));
                                end++;
                                break;

                            default:
                                break;
                        }
                    }
                    start = end;
                    if (start == -1 || start >= totlen || (inval[start] == '}'))
                        break;
                    else
                        while (inval[start] == ',' || inval[start] == ' ')
                            start++;
                }
                catch
                {
                }
            }
            return v;
        }

        public void SetStoreVars(Dictionary<string, object> vars)
        {
            if (!m_useStateSaves)
                return;
            m_lastStateSaveValues = vars;
            m_stateSaveRequired = false; //If something is setting the vars, we don't need to do a state save, as this came from a state save 
            foreach (KeyValuePair<string, object> var in vars)
            {
                if (m_Fields.ContainsKey (var.Key))
                {
                    try
                    {
                        if (m_Fields[var.Key].FieldType == typeof (LSL_Types.list))
                        {
                            string val = var.Value.ToString ();
                            int end;
                            LSL_Types.list v = ParseValueToList (val, 0, out end);
                            m_Fields[var.Key].SetValue (this, v);
                        }

                        else if (m_Fields[var.Key].FieldType == typeof (LSL_Types.LSLInteger))
                        {
                            int val = int.Parse (var.Value.ToString ());
                            m_Fields[var.Key].SetValue (this, new LSL_Types.LSLInteger (val));
                        }
                        else if (m_Fields[var.Key].FieldType == typeof (LSL_Types.LSLString))
                        {
                            string val = var.Value.ToString ();
                            m_Fields[var.Key].SetValue (this, new LSL_Types.LSLString (val));
                        }
                        else if (m_Fields[var.Key].FieldType == typeof (LSL_Types.LSLFloat))
                        {
                            float val = float.Parse (var.Value.ToString ());
                            m_Fields[var.Key].SetValue (this, new LSL_Types.LSLFloat (val));
                        }
                        else if (m_Fields[var.Key].FieldType == typeof (Int32))
                        {
                            Int32 val = Int32.Parse (var.Value.ToString ());
                            m_Fields[var.Key].SetValue (this, val);
                        }
                        else if (m_Fields[var.Key].FieldType == typeof (Double))
                        {
                            Double val = Double.Parse (var.Value.ToString ());
                            m_Fields[var.Key].SetValue (this, val);
                        }
                        else if (m_Fields[var.Key].FieldType == typeof (Single))
                        {
                            Single val = Single.Parse (var.Value.ToString ());
                            m_Fields[var.Key].SetValue (this, val);
                        }
                        else if (m_Fields[var.Key].FieldType == typeof (String))
                        {
                            String val = var.Value.ToString ();
                            m_Fields[var.Key].SetValue (this, val);
                        }
                        else if (m_Fields[var.Key].FieldType == typeof (Byte))
                        {
                            Byte val = Byte.Parse (var.Value.ToString ());
                            m_Fields[var.Key].SetValue (this, val);
                        }
                        else if (m_Fields[var.Key].FieldType == typeof (short))
                        {
                            short val = short.Parse (var.Value.ToString ());
                            m_Fields[var.Key].SetValue (this, val);
                        }
                        else if (m_Fields[var.Key].FieldType == typeof (LSL_Types.Quaternion))
                        {
                            LSL_Types.Quaternion val = new LSL_Types.Quaternion (var.Value.ToString ());
                            m_Fields[var.Key].SetValue (this, val);
                        }
                        else if (m_Fields[var.Key].FieldType == typeof (LSL_Types.Vector3))
                        {
                            LSL_Types.Vector3 val = new LSL_Types.Vector3 (var.Value.ToString ());
                            m_Fields[var.Key].SetValue (this, val);
                        }

                    }
                    catch (Exception) { }
                }
            }
        }

        public void ResetVars()
        {
            if (!m_useStateSaves)
                return;
            m_Executor.ResetStateEventFlags();
            m_stateSaveRequired = true;
            SetVars(m_InitialValues);
        }

        public void NoOp()
        {
            // Does what is says on the packet. Nowt, nada, nothing.
            // Required for insertion after a jump label to do what it says on the packet!
            // With a bit of luck the compiler may even optimize it out.
        }

        public string Name 
        {
            get
            {
                return "ScriptBase";
            }
        }

        public void Dispose()
        {
        }

        public virtual IEnumerator FireEvent (string evName, object[] parameters)
        {
            yield break;
        }
    }
}
