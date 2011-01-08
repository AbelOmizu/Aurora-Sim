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
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;

namespace OpenSim.Region.Framework.Scenes
{
    public static class TerrainUtil
    {
        public static double MetersToSphericalStrength(double size)
        {
            //return Math.Pow(2, size);
            return (size + 1) * 1.35; // MCP: a more useful brush size range
        }

        public static double SphericalFactor(double x, double y, double rx, double ry, double size)
        {
            return size * size - ((x - rx) * (x - rx) + (y - ry) * (y - ry));
        }

        public static double GetBilinearInterpolate(double x, double y, ITerrainChannel map, List<Scene> scenes)
        {
            int w = map.Width;
            int h = map.Height;

            Scene scene = null;

            if (x > w - 2)
            {
                scene = FindScene(map, scenes, Constants.RegionSize, 0);
                if(scene != null)
                {
                    //Fix this position in the new heightmap
                    x -= w;
                    map = scene.RequestModuleInterface<ITerrainChannel>();
                }
                else //1 away from the edge if we don't have a sim on this instance
                    x = w - 2;
            }
            if (y > h - 2)
            {
                scene = FindScene(map, scenes, 0, Constants.RegionSize);
                if (scene != null)
                {
                    //Fix this position in the new heightmap
                    y -= h;
                    map = scene.RequestModuleInterface<ITerrainChannel>();
                }
                else //1 away from the edge if we don't have a sim on this instance
                    y = h - 2;
            }
            if (x < 0.0)
            {
                scene = FindScene(map, scenes, -Constants.RegionSize, 0);
                if (scene != null)
                {
                    //Fix this position in the new heightmap
                    x += w;
                    map = scene.RequestModuleInterface<ITerrainChannel>();
                }
                else //1 away from the edge if we don't have a sim on this instance
                    x = 0.0;
            }
            if (y < 0.0)
            {
                scene = FindScene(map, scenes, 0, -Constants.RegionSize);
                if (scene != null)
                {
                    //Fix this position in the new heightmap
                    y += h;
                    map = scene.RequestModuleInterface<ITerrainChannel>();
                }
                else //1 away from the edge if we don't have a sim on this instance
                    y = 0.0;
            }

            if (x > map.Width - 2)
                x = map.Width - 2;
            if (x < 0)
                x = 0;
            if (y > map.Height - 2)
                y = map.Height - 2;
            if (y < 0)
                y = 0;

            const int stepSize = 1;
            double h00 = map[(int)x, (int)y];
            double h10 = map[(int)x + stepSize, (int)y];
            double h01 = map[(int)x, (int)y + stepSize];
            double h11 = map[(int)x + stepSize, (int)y + stepSize];
            double h1 = h00;
            double h2 = h10;
            double h3 = h01;
            double h4 = h11;
            double a00 = h1;
            double a10 = h2 - h1;
            double a01 = h3 - h1;
            double a11 = h1 - h2 - h3 + h4;
            double partialx = x - (int)x;
            double partialz = y - (int)y;
            double hi = a00 + (a10 * partialx) + (a01 * partialz) + (a11 * partialx * partialz);
            return hi;
        }

        private static Scene FindScene(ITerrainChannel map, List<Scene> scenes, int X, int Y)
        {
            int RegX = map.Scene.RegionInfo.RegionLocX + X;
            int RegY = map.Scene.RegionInfo.RegionLocY + Y;
            foreach (Scene scene in scenes)
            {
                if (scene.RegionInfo.RegionLocX == RegX &&
                    scene.RegionInfo.RegionLocY == RegY)
                    return scene;
            }
            return null;
        }

        private static double Noise(double x, double y)
        {
            int n = (int)x + (int)(y * 749);
            n = (n << 13) ^ n;
            return (1.0 - ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824.0);
        }

        private static double SmoothedNoise1(double x, double y)
        {
            double corners = (Noise(x - 1, y - 1) + Noise(x + 1, y - 1) + Noise(x - 1, y + 1) + Noise(x + 1, y + 1)) / 16;
            double sides = (Noise(x - 1, y) + Noise(x + 1, y) + Noise(x, y - 1) + Noise(x, y + 1)) / 8;
            double center = Noise(x, y) / 4;
            return corners + sides + center;
        }

        private static double Interpolate(double x, double y, double z)
        {
            return (x * (1.0 - z)) + (y * z);
        }

        public static double InterpolatedNoise(double x, double y)
        {
            int integer_X = (int)(x);
            double fractional_X = x - integer_X;

            int integer_Y = (int)y;
            double fractional_Y = y - integer_Y;

            double v1 = SmoothedNoise1(integer_X, integer_Y);
            double v2 = SmoothedNoise1(integer_X + 1, integer_Y);
            double v3 = SmoothedNoise1(integer_X, integer_Y + 1);
            double v4 = SmoothedNoise1(integer_X + 1, integer_Y + 1);

            double i1 = Interpolate(v1, v2, fractional_X);
            double i2 = Interpolate(v3, v4, fractional_X);

            return Interpolate(i1, i2, fractional_Y);
        }

        public static double PerlinNoise2D(double x, double y, int octaves, double persistence)
        {
            double total = 0.0;

            for (int i = 0; i < octaves; i++)
            {
                double frequency = Math.Pow(2, i);
                double amplitude = Math.Pow(persistence, i);

                total += InterpolatedNoise(x * frequency, y * frequency) * amplitude;
            }
            return total;
        }
    }
}