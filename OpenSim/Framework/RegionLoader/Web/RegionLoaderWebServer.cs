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
using System.IO;
using System.Net;
using System.Reflection;
using System.Xml;
using log4net;
using Nini.Config;

namespace OpenSim.Framework.RegionLoader.Web
{
    public class RegionLoaderWebServer : IRegionLoader
    {
        public string Name
        {
            get
            {
                return "RegionLoaderWebServer";
            }
        }

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private IConfigSource m_configSource;

        public void SetIniConfigSource(IConfigSource configSource)
        {
            m_configSource = configSource;
        }

        public RegionInfo[] LoadRegions()
        {
            if (m_configSource == null)
            {
                m_log.Error("[WEBLOADER]: Unable to load configuration source!");
                return null;
            }
            else
            {
                try
                {
                    IConfig startupConfig = (IConfig)m_configSource.Configs["Startup"];
                    string url = startupConfig.GetString("regionload_webserver_url", String.Empty).Trim();
                    if (url == String.Empty)
                    {
                        m_log.Error("[WEBLOADER]: Unable to load webserver URL - URL was empty.");
                        return null;
                    }
                    else
                    {
                        HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(url);
                        webRequest.Timeout = 30000; //30 Second Timeout

                        m_log.Debug("[WEBLOADER]: Sending Download Request...");
                        HttpWebResponse webResponse = (HttpWebResponse)webRequest.GetResponse();

                        m_log.Debug("[WEBLOADER]: Downloading Region Information From Remote Server...");
                        StreamReader reader = new StreamReader(webResponse.GetResponseStream());

                        m_log.Debug("[WEBLOADER]: Done downloading region information from server.");

                        List<RegionInfo> regionInfos = new List<RegionInfo>();

                        IConfigSource source = new IniConfigSource(new Nini.Ini.IniDocument(reader.BaseStream, Nini.Ini.IniFileType.Standard));

                        int i = 0;
                        foreach (IConfig config in source.Configs)
                        {
                            RegionInfo regionInfo = new RegionInfo("REGION CONFIG #" + (i + 1), "", false, m_configSource, config.Name);
                            regionInfos.Add(regionInfo);
                            i++;
                        }
                        return regionInfos.ToArray();
                    }
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
