using System;
using ACLLibs;

namespace AnimeStudio
{
    public static class ACLExtensions
    {
        public static void Process(this ACLClip m_ACLClip, Game game, out float[] values, out float[] times) 
        {
            if (game.Type.IsSRGroup())
            {
                var aclClip = m_ACLClip as MHYACLClip;
                SRACL.DecompressAll(aclClip.m_ClipData, out values, out times);
            }
            else
            {
                switch (m_ACLClip)
                {
                    case GIACLClip giaclClip:
                        DBACL.DecompressTracks(giaclClip.m_ClipData, giaclClip.m_DatabaseData, out values, out times);
                        break;
                    case EFACLClip efAclClip:
                        ProcessEFACL(efAclClip, out values, out times);
                        break;
                    case MHYACLClip mhyaclClip:
                        if (game.Type.IsZZZ())
                        {
                            DBACL.DecompressTracks(mhyaclClip.m_ClipData, mhyaclClip.m_databaseData, out values, out times, true);
                        }
                        else
                        {
                            ACL.DecompressAll(mhyaclClip.m_ClipData, out values, out times);
                        }

                        break;
                    default:
                        values = Array.Empty<float>();
                        times = Array.Empty<float>();
                        break;
                }
            }
        }

        private static void ProcessEFACL(EFACLClip efClip, out float[] values, out float[] times)
        {
            var buffer = efClip.m_Buffer;
            if (buffer == null)
            {
                values = Array.Empty<float>();
                times = Array.Empty<float>();
                return;
            }

            float[] transformValues = Array.Empty<float>();
            float[] transformTimes = Array.Empty<float>();
            float[] floatValues = Array.Empty<float>();
            float[] floatTimes = Array.Empty<float>();

            bool hasTransform = !buffer.TransformBufferData.IsNullOrEmpty();
            bool hasFloat = !buffer.FloatBufferData.IsNullOrEmpty();

            if (hasTransform)
            {
                try
                {
                    DBACL.DecompressTracks(buffer.TransformBufferData, Array.Empty<byte>(), out transformValues, out transformTimes);
                }
                catch
                {
                    try
                    {
                        ACL.DecompressAll(buffer.TransformBufferData, out transformValues, out transformTimes);
                    }
                    catch
                    {
                        transformValues = Array.Empty<float>();
                        transformTimes = Array.Empty<float>();
                    }
                }
            }

            if (hasFloat)
            {
                try
                {
                    DBACL.DecompressTracks(buffer.FloatBufferData, Array.Empty<byte>(), out floatValues, out floatTimes);
                }
                catch
                {
                    try
                    {
                        ACL.DecompressAll(buffer.FloatBufferData, out floatValues, out floatTimes);
                    }
                    catch
                    {
                        floatValues = Array.Empty<float>();
                        floatTimes = Array.Empty<float>();
                    }
                }
            }

            int frameCount = transformTimes.Length > 0 ? transformTimes.Length : floatTimes.Length;
            times = transformTimes.Length > 0 ? transformTimes : floatTimes;

            if (frameCount == 0)
            {
                values = Array.Empty<float>();
                efClip.m_CurveCount = 0;
                return;
            }

            int numTransformTracks = buffer.OutputTrackCount > 0 ? buffer.OutputTrackCount : (transformTimes.Length > 0 ? transformValues.Length / (frameCount * 10) : 0);
            var bindings = efClip.m_ClipBindingConstant?.genericBindings;

            if (bindings != null && bindings.Count > 0)
            {
                // Build track map: find the ACL track index for each bone path
                var trackMap = new System.Collections.Generic.Dictionary<uint, int>();
                int t = 0;
                // In Endfield, Rotation bindings list all bones in track index order
                foreach (var b in bindings)
                {
                    if (b.typeID == ClassIDType.Transform && b.attribute == 2)
                    {
                        if (!trackMap.ContainsKey(b.path))
                        {
                            trackMap[b.path] = t++;
                        }
                    }
                }
                if (trackMap.Count == 0)
                {
                    t = 0;
                    foreach (var b in bindings)
                    {
                        if (b.typeID == ClassIDType.Transform && !trackMap.ContainsKey(b.path))
                        {
                            trackMap[b.path] = t++;
                        }
                    }
                }

                int curvesPerFrame = 0;
                foreach (var b in bindings)
                {
                    curvesPerFrame += (b.typeID == ClassIDType.Transform ? (b.attribute == 2 ? 4 : 3) : 1);
                }

                efClip.m_CurveCount = (uint)curvesPerFrame;
                values = new float[frameCount * curvesPerFrame];

                int floatCurvesPerFrame = floatTimes.Length > 0 ? (floatValues.Length / floatTimes.Length) : (buffer.FloatCurveCount > 0 ? buffer.FloatCurveCount : 0);

                for (int f = 0; f < frameCount; f++)
                {
                    int dstFrameOffset = f * curvesPerFrame;
                    int srcTransFrameOffset = f * (numTransformTracks * 10);
                    int srcFloatFrameOffset = f * floatCurvesPerFrame;

                    int currCurve = 0;
                    int floatTrackIdx = 0;

                    foreach (var b in bindings)
                    {
                        if (b.typeID == ClassIDType.Transform)
                        {
                            if (trackMap.TryGetValue(b.path, out int trackIdx) && trackIdx < numTransformTracks)
                            {
                                int trackBase = srcTransFrameOffset + trackIdx * 10;
                                switch (b.attribute)
                                {
                                    case 1: // Position (Translation: x, y, z)
                                        if (trackBase + 6 < transformValues.Length)
                                        {
                                            values[dstFrameOffset + currCurve + 0] = transformValues[trackBase + 4];
                                            values[dstFrameOffset + currCurve + 1] = transformValues[trackBase + 5];
                                            values[dstFrameOffset + currCurve + 2] = transformValues[trackBase + 6];
                                        }
                                        currCurve += 3;
                                        break;

                                    case 2: // Rotation (Quaternion: x, y, z, w)
                                        if (trackBase + 3 < transformValues.Length)
                                        {
                                            values[dstFrameOffset + currCurve + 0] = transformValues[trackBase + 0];
                                            values[dstFrameOffset + currCurve + 1] = transformValues[trackBase + 1];
                                            values[dstFrameOffset + currCurve + 2] = transformValues[trackBase + 2];
                                            values[dstFrameOffset + currCurve + 3] = transformValues[trackBase + 3];
                                        }
                                        currCurve += 4;
                                        break;

                                    case 3: // Scale (x, y, z)
                                        if (trackBase + 9 < transformValues.Length)
                                        {
                                            values[dstFrameOffset + currCurve + 0] = transformValues[trackBase + 7];
                                            values[dstFrameOffset + currCurve + 1] = transformValues[trackBase + 8];
                                            values[dstFrameOffset + currCurve + 2] = transformValues[trackBase + 9];
                                        }
                                        currCurve += 3;
                                        break;

                                    case 4: // Euler (x, y, z)
                                        if (trackBase + 2 < transformValues.Length)
                                        {
                                            values[dstFrameOffset + currCurve + 0] = transformValues[trackBase + 0];
                                            values[dstFrameOffset + currCurve + 1] = transformValues[trackBase + 1];
                                            values[dstFrameOffset + currCurve + 2] = transformValues[trackBase + 2];
                                        }
                                        currCurve += 3;
                                        break;

                                    default:
                                        currCurve += (b.attribute == 2 ? 4 : 3);
                                        break;
                                }
                            }
                            else
                            {
                                currCurve += (b.attribute == 2 ? 4 : 3);
                            }
                        }
                        else
                        {
                            // Float / Scalar curve
                            if (srcFloatFrameOffset + floatTrackIdx < floatValues.Length)
                            {
                                values[dstFrameOffset + currCurve] = floatValues[srcFloatFrameOffset + floatTrackIdx];
                            }
                            floatTrackIdx++;
                            currCurve++;
                        }
                    }
                }
            }
            else
            {
                // Fallback if no genericBindings are present
                int transformCurveCount = transformTimes.Length > 0 ? transformValues.Length / frameCount : 0;
                int floatCurveCount = floatTimes.Length > 0 ? floatValues.Length / frameCount : 0;
                int totalCurveCount = transformCurveCount + floatCurveCount;

                values = new float[frameCount * totalCurveCount];
                for (int i = 0; i < frameCount; i++)
                {
                    int dstOffset = i * totalCurveCount;
                    int srcTransOffset = i * transformCurveCount;
                    int srcFloatOffset = i * floatCurveCount;

                    if (transformCurveCount > 0)
                    {
                        Array.Copy(transformValues, srcTransOffset, values, dstOffset, transformCurveCount);
                    }
                    if (floatCurveCount > 0 && srcFloatOffset + floatCurveCount <= floatValues.Length)
                    {
                        Array.Copy(floatValues, srcFloatOffset, values, dstOffset + transformCurveCount, floatCurveCount);
                    }
                }
                efClip.m_CurveCount = (uint)totalCurveCount;
            }
        }
    }
}
