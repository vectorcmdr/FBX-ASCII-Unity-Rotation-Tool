// "FBX ASCII Rebake Tool"
// ASCII FBX Transform Baker + Unity Prefab Transform Resetter
// by: vector_cmdr (https://github.com/vectorcmdr)
//
// This tool processes ASCII FBX files in the current directory,
// and bakes original local rotation, scale etc. (Properties70)
// into geometry properties (Geometry) and resets the properties.
// It also processes Unity prefab files that correspond to the
// FBX files and resets m_LocalRotation to identity quaternion,
// m_LocalScale to (1,1,1), m_LocalEulerAnglesHint to (0,0,0).
//
// It then writes the modified files to a subdirectory named "baked".
//
// License: MIT License (https://opensource.org/licenses/MIT)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace RebaseFBX
{
    class Program
    {
        const double NZT = 1e-6;

        static int Main(string[] args)
        {
            string dir = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
            if (!Directory.Exists(dir)) { Console.Error.WriteLine($"Directory not found: {dir}"); return 1; }

            string outDir = Path.Combine(dir, "baked");
            Directory.CreateDirectory(outDir);
            int ok = 0, fail = 0;

            // Process FBX files
            string[] fbxFiles = Directory.GetFiles(dir, "*.fbx", SearchOption.TopDirectoryOnly);
            foreach (string path in fbxFiles)
            {
                string fname = Path.GetFileName(path);
                Console.WriteLine($"\n--- {fname} ---");
                try
                {
                    if (IsBinary(path)) { Console.WriteLine("  SKIPPED (binary)."); continue; }
                    string[] lines = File.ReadAllLines(path);
                    int n = ProcessFbx(lines);
                    Console.WriteLine(n > 0 ? $"  Baked {n} submesh(es)." : "  Nothing to bake.");
                    File.WriteAllLines(Path.Combine(outDir, fname), lines);
                    Console.WriteLine($"  -> {Path.Combine(outDir, fname)}"); ok++;
                }
                catch (Exception ex) { Console.Error.WriteLine($"  FAILED: {ex.Message}\n{ex.StackTrace}"); fail++; }
            }

            // Process Unity .prefab files
            string[] prefabFiles = Directory.GetFiles(dir, "*.prefab", SearchOption.TopDirectoryOnly);
            foreach (string path in prefabFiles)
            {
                string fname = Path.GetFileName(path);
                Console.WriteLine($"\n--- {fname} ---");
                try
                {
                    string[] lines = File.ReadAllLines(path);
                    int n = ProcessPrefab(lines);
                    Console.WriteLine(n > 0 ? $"  Reset {n} Transform(s)." : "  Nothing to reset.");
                    File.WriteAllLines(Path.Combine(outDir, fname), lines);
                    Console.WriteLine($"  -> {Path.Combine(outDir, fname)}"); ok++;
                }
                catch (Exception ex) { Console.Error.WriteLine($"  FAILED: {ex.Message}\n{ex.StackTrace}"); fail++; }
            }

            if (fbxFiles.Length == 0 && prefabFiles.Length == 0)
                Console.WriteLine("No .fbx or .prefab files found.");

            Console.WriteLine($"\nDone. OK={ok} FAIL={fail}");
            return fail > 0 ? 1 : 0;
        }

        /// <summary>
        /// Determines if the specified FBX file is in binary format.
        /// Reads the first 20 bytes of the file and checks for the "Kaydara FBX Binary" signature.
        /// Returns true if the file is binary, otherwise false.
        /// </summary>
        /// <param name="p">Path to the FBX file.</param>
        /// <returns>True if the file is binary; false if ASCII or unreadable.</returns>
        static bool IsBinary(string p)
        {
            try
            {
                byte[] h = new byte[20];
                using var f = File.OpenRead(p);
                f.Read(h, 0, 20);
                return Encoding.ASCII.GetString(h).StartsWith("Kaydara FBX Binary");
            }
            catch{ return false; }
        }

        /// <summary>
        /// Matches inline: m_LocalRotation: {x: 0.123, y: 0, z: 0, w: 1}
        /// </summary>
        static readonly Regex RxRotInline = new Regex(
            @"^(\s*m_LocalRotation:\s*)\{x:\s*[^,]+,\s*y:\s*[^,]+,\s*z:\s*[^,]+,\s*w:\s*[^\}]+\}",
            RegexOptions.Compiled);

        /// <summary>
        /// Matches inline: m_LocalScale: {x: 1, y: 1, z: 1}
        /// </summary>
        static readonly Regex RxScaleInline = new Regex(
            @"^(\s*m_LocalScale:\s*)\{x:\s*[^,]+,\s*y:\s*[^,]+,\s*z:\s*[^\}]+\}",
            RegexOptions.Compiled);

        /// <summary>
        /// Matches inline: m_LocalEulerAnglesHint: {x: 0, y: 90, z: 0}
        /// </summary>
        static readonly Regex RxEulerInline = new Regex(
            @"^(\s*m_LocalEulerAnglesHint:\s*)\{x:\s*[^,]+,\s*y:\s*[^,]+,\s*z:\s*[^\}]+\}",
            RegexOptions.Compiled);

        /// <summary>
        /// Processes a Unity .prefab YAML file for a rebaked rebased FBX.
        /// Finds all Transform / RectTransform components and resets
        /// m_LocalRotation, m_LocalScale, and m_LocalEulerAnglesHint.
        /// Preserves all other content byte-for-byte.
        /// </summary>
        static int ProcessPrefab(string[] L)
        {
            int resetCount = 0;

            for (int i = 0; i < L.Length; i++)
            {
                string trimmed = L[i].TrimStart();

                // m_LocalRotation
                // Inline form: m_LocalRotation: {x: .., y: .., z: .., w: ..}
                if (trimmed.StartsWith("m_LocalRotation:"))
                {
                    var m = RxRotInline.Match(L[i]);
                    if (m.Success)
                    {
                        string prefix = m.Groups[1].Value;
                        string newVal = "{x: 0, y: 0, z: 0, w: 1}";
                        L[i] = prefix + newVal;
                        resetCount++;
                    }
                    else if (trimmed == "m_LocalRotation:")
                    {
                        // Multi-line form:
                        //   m_LocalRotation:
                        //     x: 0
                        //     y: 0
                        //     z: 0
                        //     w: 1
                        ResetMultiLineQuat(L, i + 1);
                        resetCount++;
                    }
                }

                // m_LocalScale
                else if (trimmed.StartsWith("m_LocalScale:"))
                {
                    var m = RxScaleInline.Match(L[i]);
                    if (m.Success)
                    {
                        string prefix = m.Groups[1].Value;
                        L[i] = prefix + "{x: 1, y: 1, z: 1}";
                    }
                    else if (trimmed == "m_LocalScale:")
                    {
                        ResetMultiLineVec3(L, i + 1, 1, 1, 1);
                    }
                }

                // m_LocalEulerAnglesHint
                else if (trimmed.StartsWith("m_LocalEulerAnglesHint:"))
                {
                    var m = RxEulerInline.Match(L[i]);
                    if (m.Success)
                    {
                        string prefix = m.Groups[1].Value;
                        L[i] = prefix + "{x: 0, y: 0, z: 0}";
                    }
                    else if (trimmed == "m_LocalEulerAnglesHint:")
                    {
                        ResetMultiLineVec3(L, i + 1, 0, 0, 0);
                    }
                }
            }

            return resetCount;
        }

        /// <summary>
        /// Resets multi-line quaternion (x, y, z, w) starting at line 'start'.
        /// </summary>
        static void ResetMultiLineQuat(string[] L, int start)
        {
            var map = new Dictionary<string, string>
            { { "x", "0" }, { "y", "0" }, { "z", "0" }, { "w", "1" } };
            for (int i = start; i < Math.Min(start + 6, L.Length); i++)
            {
                string t = L[i].TrimStart();
                foreach (var kv in map)
                {
                    if (t.StartsWith(kv.Key + ":"))
                    {
                        int colon = L[i].IndexOf(':');
                        L[i] = L[i].Substring(0, colon + 1) + " " + kv.Value;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Resets multi-line vector3 (x, y, z) starting at line 'start'.
        /// </summary>
        static void ResetMultiLineVec3(string[] L, int start, double x, double y, double z)
        {
            var map = new Dictionary<string, string>
            { { "x", Fmt(x) }, { "y", Fmt(y) }, { "z", Fmt(z) } };
            for (int i = start; i < Math.Min(start + 5, L.Length); i++)
            {
                string t = L[i].TrimStart();
                foreach (var kv in map)
                {
                    if (t.StartsWith(kv.Key + ":"))
                    {
                        int colon = L[i].IndexOf(':');
                        L[i] = L[i].Substring(0, colon + 1) + " " + kv.Value;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Represents a model entry in the FBX file.
        /// </summary>
        /// <remarks>
        /// This struct is used to store information about a model node parsed from the FBX "Objects" section.
        /// <list type="bullet">
        /// <item>
        /// <description><see cref="Id"/>: The unique identifier of the model node.</description>
        /// </item>
        /// <item>
        /// <description><see cref="Name"/>: The name of the model node (as parsed from the FBX).</description>
        /// </item>
        /// <item>
        /// <description><see cref="Ps"/>: The start line index of the Properties section for this model in the FBX file, or -1 if not found.</description>
        /// </item>
        /// <item>
        /// <description><see cref="Pe"/>: The end line index of the Properties section for this model in the FBX file, or -1 if not found.</description>
        /// </item>
        /// </list>
        /// </remarks>
        struct MdlE { public long Id; public string Name; public int Ps, Pe; }

        /// <summary>
        /// Represents a geometry entry in the FBX file.
        /// </summary>
        /// <remarks>
        /// This struct is used to store information about a geometry node parsed from the FBX "Objects" section.
        /// <list type="bullet">
        /// <item>
        /// <description><see cref="Id"/>: The unique identifier of the geometry node.</description>
        /// </item>
        /// <item>
        /// <description><see cref="Bs"/>: The start line index of the geometry block in the FBX file (inclusive).</description>
        /// </item>
        /// <item>
        /// <description><see cref="Be"/>: The end line index of the geometry block in the FBX file (inclusive).</description>
        /// </item>
        /// </list>
        /// </remarks>
        struct GeoE { public long Id; public int Bs, Be; }

        /// <summary>
        /// Processes an ASCII FBX file's contents, baking transform properties into geometry and resetting transform properties.
        /// </summary>
        /// <param name="L">The lines of the FBX file to process.</param>
        /// <remarks>
        /// <para>
        /// This method parses the "Objects" and "Connections" sections of the FBX file to identify model and geometry nodes,
        /// and maps geometry nodes to their parent model nodes. For each geometry-model pair, it:
        /// </para>
        /// <para>
        /// The method preserves the original file structure and formatting as much as possible, only modifying the relevant transform and geometry data in-place.
        /// </para>
        /// <para>
        /// If no eligible geometry is found or no transforms need baking, returns 0.
        /// </para>
        /// <returns>
        /// The number of geometry submeshes that were baked (i.e. had transforms applied and reset).
        /// </returns>
        /// </remarks>
        static int ProcessFbx(string[] L)
        {
            FindSec(L, "Objects", out int oS, out int oE); if (oS < 0) return 0;
            FindSec(L, "Connections", out int cS, out int cE);

            var mdls = new Dictionary<long, MdlE>();
            for (int i = oS; i <= oE; i++)
            {
                string t = L[i].TrimStart(); if (!t.StartsWith("Model:")) continue;
                long id = PId(t); if (id == 0) continue;
                int bo = FB(L, i, oE); if (bo < 0) continue;
                int bc = FC(L, bo); if (bc < 0) continue;
                var me = new MdlE { Id = id, Name = EName(t), Ps = -1, Pe = -1 };
                for (int j = bo + 1; j < bc; j++)
                {
                    string pt = L[j].TrimStart();
                    if (pt.StartsWith("Properties70:") || pt.StartsWith("Properties60:"))
                    {
                        int po = FB(L, j, bc);
                        if (po >= 0)
                        {
                            int pc = FC(L, po);
                            if (pc >= 0) { me.Ps = po; me.Pe = pc; }
                        }
                        break;
                    }
                }
                mdls[id] = me; i = bc;
            }

            var geos = new Dictionary<long, GeoE>();
            for (int i = oS; i <= oE; i++)
            {
                string t = L[i].TrimStart(); if (!t.StartsWith("Geometry:") || !t.Contains("\"Mesh\"")) continue;
                long gid = PId(t); if (gid == 0) continue;
                int bo = FB(L, i, oE); if (bo < 0) continue;
                int bc = FC(L, bo); if (bc < 0) continue;
                geos[gid] = new GeoE { Id = gid, Bs = bo + 1, Be = bc - 1 }; i = bc;
            }

            var g2m = new Dictionary<long, long>();
            if (cS >= 0) for (int i = cS; i <= cE; i++)
            {
                string t = L[i].TrimStart(); int ci = t.IndexOf("C:", StringComparison.Ordinal); if (ci < 0) continue;
                string[] p = CSV(t.Substring(ci + 2)); if (p.Length < 3 || UQ(p[0]) != "OO") continue;
                long ch = PL(p[1]), pa = PL(p[2]);
                if (ch != 0 && pa != 0 && geos.ContainsKey(ch) && mdls.ContainsKey(pa)) g2m[ch] = pa;
            }

            int baked = 0;
            foreach (var kvp in g2m)
            {
                long gid = kvp.Key, mid = kvp.Value;
                var mdl = mdls[mid]; var geo = geos[gid];
                if (mdl.Ps < 0) continue;
                int ps = mdl.Ps, pe = mdl.Pe;

                Vec3 rot = RV3(L, ps, pe, "Lcl Rotation", V0);
                Vec3 scl = RV3(L, ps, pe, "Lcl Scaling", V1);
                Vec3 tr = RV3(L, ps, pe, "Lcl Translation", V0);
                Vec3 pre = RV3(L, ps, pe, "PreRotation", V0);
                Vec3 pst = RV3(L, ps, pe, "PostRotation", V0);
                Vec3 rof = RV3(L, ps, pe, "RotationOffset", V0);
                Vec3 rpv = RV3(L, ps, pe, "RotationPivot", V0);
                Vec3 sof = RV3(L, ps, pe, "ScalingOffset", V0);
                Vec3 spv = RV3(L, ps, pe, "ScalingPivot", V0);
                Vec3 gT = RV3(L, ps, pe, "GeometricTranslation", V0);
                Vec3 gR = RV3(L, ps, pe, "GeometricRotation", V0);
                Vec3 gS = RV3(L, ps, pe, "GeometricScaling", V1);

                int ro = 0;
                int roLn = FPL(L, ps, pe, "RotationOrder");
                if (roLn >= 0)
                {
                    var rp = CSV(L[roLn].Substring(L[roLn].IndexOf(':') + 1));
                    if (rp.Length >= 1) int.TryParse(rp[rp.Length - 1].Trim(), out ro);
                }

                bool any = !IsZ(rot) || !Is1(scl) || !IsZ(pre) || !IsZ(pst) || !IsZ(gT) || !IsZ(gR) || !Is1(gS);
                if (!any) continue;

                Console.Write($"  [{mdl.Name}] o={RN(ro)}");
                if (!IsZ(rot)) Console.Write($" r=({rot.X:G5},{rot.Y:G5},{rot.Z:G5})");
                if (!Is1(scl)) Console.Write($" s=({scl.X:G5},{scl.Y:G5},{scl.Z:G5})");
                if (!IsZ(pre)) Console.Write($" pre=({pre.X:G5},{pre.Y:G5},{pre.Z:G5})");
                if (!IsZ(pst)) Console.Write($" pst=({pst.X:G5},{pst.Y:G5},{pst.Z:G5})");
                if (!IsZ(gR)) Console.Write($" gR=({gR.X:G5},{gR.Y:G5},{gR.Z:G5})");
                if (!Is1(gS)) Console.Write($" gS=({gS.X:G5},{gS.Y:G5},{gS.Z:G5})");

                Mat4 mNode = Mat4.Tr(tr) * Mat4.Tr(rof) * Mat4.Tr(rpv)
                    * Mat4.Euler(pre, 0) * Mat4.Euler(rot, ro) * Mat4.Invert(Mat4.Euler(pst, 0))
                    * Mat4.Tr(Neg(rpv)) * Mat4.Tr(sof) * Mat4.Tr(spv) * Mat4.Scale(scl) * Mat4.Tr(Neg(spv));
                Mat4 mGeo = Mat4.Tr(gT) * Mat4.Euler(gR, 0) * Mat4.Scale(gS);
                Mat4 mClean = Mat4.Tr(tr) * Mat4.Tr(rof) * Mat4.Tr(sof);
                Mat4 mBake = Mat4.Invert(mClean) * mNode * mGeo;

                double det3 = Det3(mBake);
                bool mirr = det3 < 0;
                if (mirr) Console.Write(" [MIRROR]");
                Console.WriteLine();

                Mat4 nBake = NormalMat(mBake);
                XfArr(L, geo.Bs, geo.Be, "Vertices", mBake, false);

                List<int[]> polys = mirr ? ParsePolys(L, geo.Bs, geo.Be) : null;
                ProcLE(L, geo.Bs, geo.Be, "LayerElementNormal", "Normals", nBake, mirr, polys);
                ProcLE(L, geo.Bs, geo.Be, "LayerElementTangent", "Tangents", nBake, mirr, polys);
                ProcLE(L, geo.Bs, geo.Be, "LayerElementBinormal", "Binormals", nBake, mirr, polys);

                if (mirr)
                {
                    ReordLE(L, geo.Bs, geo.Be, "LayerElementUV", "UV", polys, 2);
                    ReordLE(L, geo.Bs, geo.Be, "LayerElementColor", "Colors", polys, 4);
                    ReverseWind(L, geo.Bs, geo.Be);
                }

                int nzf = FixNZ(L, geo.Bs, geo.Be);
                if (nzf > 0) Console.WriteLine($"    -> Fixed {nzf} near-zero normal(s)");

                WV3(L, ps, pe, "Lcl Rotation", 0, 0, 0);
                WV3(L, ps, pe, "Lcl Scaling", 1, 1, 1);
                WV3(L, ps, pe, "PreRotation", 0, 0, 0);
                WV3(L, ps, pe, "PostRotation", 0, 0, 0);
                WV3(L, ps, pe, "GeometricTranslation", 0, 0, 0);
                WV3(L, ps, pe, "GeometricRotation", 0, 0, 0);
                WV3(L, ps, pe, "GeometricScaling", 1, 1, 1);
                baked++;
            }
            return baked;
        }

        /// <summary>
        /// Processes LayerElement arrays in FBX geometry, applies transformation matrix
        /// and optionally reorders elements for mirrored meshes.
        /// </summary>
        static void ProcLE(string[] L, int rs, int re,
            string leName, string arrName, Mat4 nm, bool reord, List<int[]> polys)
        {
            for (int i = rs; i <= re; i++)
            {
                string t = L[i].TrimStart();
                if (!t.StartsWith(leName)) continue;
                int bo = FB(L, i, re); if (bo < 0) continue;
                int bc = FC(L, bo); if (bc < 0) continue;
                XfArr(L, bo + 1, bc - 1, arrName, nm, true);
                if (reord && polys != null)
                {
                    string map = RSt(L, bo + 1, bc - 1, "MappingInformationType");
                    if (map.IndexOf("ByPolygonVertex", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string reft = RSt(L, bo + 1, bc - 1, "ReferenceInformationType");
                        if (reft.IndexOf("IndexToDirect", StringComparison.OrdinalIgnoreCase) >= 0)
                            ReordI(L, bo + 1, bc - 1, arrName + "Index", polys, 1);
                        else
                            ReordD(L, bo + 1, bc - 1, arrName, polys, 3);
                    }
                }
                i = bc;
            }
        }

        /// <summary>
        /// Reorders LayerElement arrays in FBX geometry for mirrored meshes, based on polygon winding and stride.
        /// </summary>
        static void ReordLE(string[] L, int rs, int re,
            string leName, string arrName, List<int[]> polys, int stride)
        {
            if (polys == null) return;
            for (int i = rs; i <= re; i++)
            {
                string t = L[i].TrimStart();
                if (!t.StartsWith(leName)) continue;
                int bo = FB(L, i, re); if (bo < 0) continue;
                int bc = FC(L, bo); if (bc < 0) continue;
                string map = RSt(L, bo + 1, bc - 1, "MappingInformationType");
                if (map.IndexOf("ByPolygonVertex", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    i = bc;
                    continue;
                }
                string reft = RSt(L, bo + 1, bc - 1, "ReferenceInformationType");
                if (reft.IndexOf("IndexToDirect", StringComparison.OrdinalIgnoreCase) >= 0)
                    ReordI(L, bo + 1, bc - 1, arrName + "Index", polys, 1);
                else
                    ReordD(L, bo + 1, bc - 1, arrName, polys, stride);
                i = bc;
            }
        }

        /// <summary>
        /// Parses the PolygonVertexIndex array in FBX geometry and returns a list of polygon index arrays.
        /// </summary>
        static List<int[]> ParsePolys(string[] L, int rs, int re)
        {
            int[] raw = RdI(L, rs, re, "PolygonVertexIndex"); if (raw == null) return null;
            var polys = new List<int[]>(); int ps = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] < 0)
                {
                    int len = i - ps + 1;
                    int[] p = new int[len];
                    for (int j = 0; j < len; j++) p[j] = ps + j;
                    polys.Add(p); ps = i + 1;
                }
            }
            return polys;
        }

        /// <summary>
        /// Reverses the winding order of polygons in the PolygonVertexIndex array for mirrored meshes.
        /// </summary>
        static void ReverseWind(string[] L, int rs, int re)
        {
            int[] v = RdI(L, rs, re, "PolygonVertexIndex", out var li); if (v == null) return;
            int ps = 0, pc = 0;
            for (int i = 0; i < v.Length; i++)
                if (v[i] < 0)
                {
                    int lr = -(v[i] + 1); int len = i - ps + 1;
                    int[] pv = new int[len];
                    for (int j = 0; j < len - 1; j++) pv[j] = v[ps + j];
                    pv[len - 1] = lr; Array.Reverse(pv);
                    for (int j = 0; j < len - 1; j++) v[ps + j] = pv[j];
                    v[i] = -(pv[len - 1] + 1); ps = i + 1; pc++;
                }
            WrI(L, v, li); Console.WriteLine($"    -> Reversed winding on {pc} polygon(s)");
        }

        /// <summary>
        /// Reorders direct-mapped LayerElement arrays for mirrored meshes, swapping elements within each polygon.
        /// </summary>
        static void ReordD(string[] L, int rs, int re, string name, List<int[]> polys, int stride)
        {
            double[] v = RdD(L, rs, re, name, out var li); if (v == null) return;
            foreach (var poly in polys)
            {
                for (int a = 0, b = poly.Length - 1; a < b; a++, b--)
                {
                    int ai = poly[a] * stride, bi = poly[b] * stride;
                    for (int s = 0; s < stride; s++)
                    {
                        double tmp = v[ai + s];
                        v[ai + s] = v[bi + s];
                        v[bi + s] = tmp;
                    }
                }
            }
            WrD(L, v, li);
        }

        /// <summary>
        /// Reorders index-mapped LayerElement arrays for mirrored meshes, swapping indices within each polygon.
        /// </summary>
        static void ReordI(string[] L, int rs, int re, string name, List<int[]> polys, int stride)
        {
            int[] v = RdI(L, rs, re, name, out var li); if (v == null) return;
            foreach (var poly in polys)
            {
                for (int a = 0, b = poly.Length - 1; a < b; a++, b--)
                {
                    int ai = poly[a] * stride, bi = poly[b] * stride;
                    for (int s = 0; s < stride; s++)
                    {
                        int tmp = v[ai + s];
                        v[ai + s] = v[bi + s];
                        v[bi + s] = tmp;
                    }
                }
            }
            WrI(L, v, li);
        }

        /// <summary>
        /// Fixes near-zero normals in LayerElementNormal arrays, normalizes them, and replaces invalid normals.
        /// </summary>
        static int FixNZ(string[] L, int rs, int re)
        {
            int total = 0;
            for (int i = rs; i <= re; i++)
            {
                string t = L[i].TrimStart();
                if (!t.StartsWith("LayerElementNormal")) continue;
                int bo = FB(L, i, re); if (bo < 0) continue;
                int bc = FC(L, bo); if (bc < 0) continue;
                total += FixNZArr(L, bo + 1, bc - 1, "Normals"); i = bc;
            }
            return total;
        }

        /// <summary>
        /// Fixes near-zero values in a specific normal array, normalizes, and replaces invalid normals.
        /// </summary>
        static int FixNZArr(string[] L, int rs, int re, string name)
        {
            double[] v = RdD(L, rs, re, name, out var li); if (v == null) return 0;
            int fc = 0;
            for (int i = 0; i + 2 < v.Length; i += 3)
            {
                double x = v[i], y = v[i + 1], z = v[i + 2];
                if (Math.Abs(x) < NZT) x = 0;
                if (Math.Abs(y) < NZT) y = 0;
                if (Math.Abs(z) < NZT) z = 0;
                double len = Math.Sqrt(x * x + y * y + z * z);
                if (len < NZT) { v[i] = 0; v[i + 1] = 1; v[i + 2] = 0; fc++; }
                else if (Math.Abs(len - 1.0) > 0.001) { v[i] = x / len; v[i + 1] = y / len; v[i + 2] = z / len; fc++; }
                else { v[i] = x; v[i + 1] = y; v[i + 2] = z; }
            }
            if (fc > 0) WrD(L, v, li);
            return fc;
        }

        /// <summary>
        /// Represents line information for array data in FBX geometry.
        /// </summary>
        /// <remarks>
        /// The <c>LI</c> struct is used to track the location and formatting of array data (such as vertices or normals)
        /// within the lines of an FBX ASCII file. This information is necessary for reading, transforming, and writing
        /// back the array data while preserving the original file's formatting.
        /// </remarks>
        /// <param name="Ln">The line number in the file where this array segment is located.</param>
        /// <param name="Pfx">The prefix string (indentation and any leading tokens) for the line.</param>
        /// <param name="Cnt">The number of values on this line segment.</param>
        /// <param name="TC">True if the line ends with a trailing comma, otherwise false.</param>
        struct LI
        {
            public int Ln;      // Line number in the file
            public string Pfx;  // Prefix (indentation, etc.)
            public int Cnt;     // Number of values on this line
            public bool TC;     // Trailing comma present
        }

        /// <summary>
        /// Applies a transformation matrix to an array of FBX geometry elements (vertices or normals).
        /// </summary>
        static void XfArr(string[] L, int rs, int re, string name, Mat4 m, bool isN)
        {
            double[] v = RdD(L, rs, re, name, out var li); if (v == null) return;
            for (int i = 0; i + 2 < v.Length; i += 3)
            {
                double x = v[i], y = v[i + 1], z = v[i + 2];
                if (isN)
                {
                    double tx = m.M00 * x + m.M01 * y + m.M02 * z;
                    double ty = m.M10 * x + m.M11 * y + m.M12 * z;
                    double tz = m.M20 * x + m.M21 * y + m.M22 * z;
                    double len = Math.Sqrt(tx * tx + ty * ty + tz * tz);
                    if (len > 1e-14) { tx /= len; ty /= len; tz /= len; }
                    v[i] = tx; v[i + 1] = ty; v[i + 2] = tz;
                }
                else
                {
                    v[i] = m.M00 * x + m.M01 * y + m.M02 * z + m.M03;
                    v[i + 1] = m.M10 * x + m.M11 * y + m.M12 * z + m.M13;
                    v[i + 2] = m.M20 * x + m.M21 * y + m.M22 * z + m.M23;
                }
            }
            WrD(L, v, li);
        }

        /// <summary>
        /// Reads a double array from FBX geometry, returning the values and line info for writing back.
        /// </summary>
        static double[] RdD(string[] L, int rs, int re, string name, out List<LI> lis)
        {
            lis = null; int hl = -1;
            for (int i = rs; i <= re; i++)
            {
                string t = L[i].TrimStart();
                if (t.StartsWith(name + ":") && t.Contains("*")) { hl = i; break; }
            }
            if (hl < 0) return null;
            int ob = FB(L, hl, re + 1); if (ob < 0) return null;
            int cb = FC(L, ob); if (cb < 0) return null;
            int al = -1;
            for (int i = ob + 1; i < cb; i++)
            {
                if (L[i].TrimStart().StartsWith("a:")) { al = i; break; }
            }
            if (al < 0) return null;
            var all = new List<double>(); lis = new List<LI>();
            for (int i = al; i <= cb - 1; i++)
            {
                string raw = L[i]; string pfx, cnt;
                if (i == al)
                {
                    int ap = raw.IndexOf("a:", StringComparison.Ordinal);
                    pfx = raw.Substring(0, ap + 2);
                    cnt = raw.Substring(ap + 2);
                }
                else
                {
                    int ind = 0;
                    while (ind < raw.Length && (raw[ind] == ' ' || raw[ind] == '\t')) ind++;
                    pfx = raw.Substring(0, ind);
                    cnt = raw.Substring(ind);
                }
                
                bool tc = cnt.TrimEnd().EndsWith(","); int c = 0;
                foreach (string n in cnt.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string s = n.Trim();
                    if (s.Length > 0 && double.TryParse(s, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double val))
                    {
                        all.Add(val); c++;
                    }
                }
                lis.Add(new LI { Ln = i, Pfx = pfx, Cnt = c, TC = tc });
            }
            return all.ToArray();
        }

        /// <summary>
        /// Writes a double array back to FBX geometry using the provided line info.
        /// </summary>
        static void WrD(string[] L, double[] v, List<LI> lis)
        {
            int vi = 0;
            for (int i = 0; i < lis.Count; i++)
            {
                var inf = lis[i]; var sb = new StringBuilder();
                sb.Append(inf.Pfx); if (i == 0) sb.Append(' ');
                for (int j = 0; j < inf.Cnt; j++) { if (j > 0) sb.Append(','); sb.Append(Fmt(v[vi++])); }
                if (inf.TC) sb.Append(',');
                L[inf.Ln] = sb.ToString();
            }
        }

        /// <summary>
        /// Reads an integer array from FBX geometry, returning the values and line info for writing back.
        /// </summary>
        static int[] RdI(string[] L, int rs, int re, string name) => RdI(L, rs, re, name, out _);

        static int[] RdI(string[] L, int rs, int re, string name, out List<LI> lis)
        {
            lis = null; int hl = -1;
            for (int i = rs; i <= re; i++)
            {
                string t = L[i].TrimStart();
                if (t.StartsWith(name + ":") && t.Contains("*")) { hl = i; break; }
            }
            if (hl < 0) return null;
            int ob = FB(L, hl, re + 1); if (ob < 0) return null;
            int cb = FC(L, ob);
            if (cb < 0) return null;
            int al = -1;
            for (int i = ob + 1; i < cb; i++)
            {
                if (L[i].TrimStart().StartsWith("a:")) { al = i; break; }
            }
            if (al < 0) return null;
            var all = new List<int>(); lis = new List<LI>();
            for (int i = al; i <= cb - 1; i++)
            {
                string raw = L[i];
                string pfx, cnt;
                if (i == al)
                {
                    int ap = raw.IndexOf("a:", StringComparison.Ordinal);
                    pfx = raw.Substring(0, ap + 2);
                    cnt = raw.Substring(ap + 2);
                }
                else
                {
                    int ind = 0;
                    while (ind < raw.Length && (raw[ind] == ' ' || raw[ind] == '\t')) ind++;
                    pfx = raw.Substring(0, ind);
                    cnt = raw.Substring(ind);
                }
                bool tc = cnt.TrimEnd().EndsWith(","); int c = 0;
                foreach (string n in cnt.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string s = n.Trim();
                    if (s.Length > 0 && int.TryParse(s, NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int val))
                    {
                        all.Add(val); c++;
                    }
                }
                lis.Add(new LI { Ln = i, Pfx = pfx, Cnt = c, TC = tc });
            }
            return all.ToArray();
        }

        /// <summary>
        /// Writes an integer array back to FBX geometry using the provided line info.
        /// </summary>
        static void WrI(string[] L, int[] v, List<LI> lis)
        {
            int vi = 0;
            for (int i = 0; i < lis.Count; i++)
            {
                var inf = lis[i]; var sb = new StringBuilder();
                sb.Append(inf.Pfx); if (i == 0) sb.Append(' ');
                for (int j = 0; j < inf.Cnt; j++)
                {
                    if (j > 0) sb.Append(','); sb.Append(v[vi++].ToString(CultureInfo.InvariantCulture));
                }
                if (inf.TC) sb.Append(',');
                L[inf.Ln] = sb.ToString();
            }
        }

        /// <summary>
        /// Reads a string property value from FBX geometry between specified lines.
        /// </summary>
        static string RSt(string[] L, int s, int e, string pn)
        {
            for (int i = s; i <= e; i++)
            {
                string t = L[i].TrimStart();
                if (t.StartsWith(pn + ":"))
                {
                    int q1 = t.IndexOf('"');
                    if (q1 >= 0)
                    {
                        int q2 = t.IndexOf('"', q1 + 1);
                        if (q2 > q1) return t.Substring(q1 + 1, q2 - q1 - 1);
                    }
                    return t.Substring(t.IndexOf(':') + 1).Trim();
                }
            }
            return "";
        }

        // ==============
        // Shared Helpers
        // ==============

        /// <summary>
        /// Provides commonly used constant vectors for FBX transform operations.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="V0"/> is a zero vector (0, 0, 0), typically used as a default for translation or rotation.
        /// <see cref="V1"/> is a unit vector (1, 1, 1), typically used as a default for scaling.
        /// </para>
        /// These constants are used throughout the FBX processing logic to represent default or identity values
        /// for vector-based FBX properties.
        /// </remarks>
        static readonly Vec3 V0 = new Vec3(0, 0, 0), V1 = new Vec3(1, 1, 1);

        /// <summary>
        /// Determines whether the given <see cref="Vec3"/> is effectively a zero vector.
        /// </summary>
        /// <param name="v">The vector to check.</param>
        /// <returns>
        /// True if all components (X, Y, Z) are within 1e-10 of zero; otherwise, false.
        /// </returns>
        /// <remarks>
        /// This method is used to test if a vector is close enough to zero to be considered as such
        /// for the purposes of FBX transform property comparisons and resets.
        /// </remarks>
        static bool IsZ(Vec3 v) => Math.Abs(v.X) < 1e-10 && Math.Abs(v.Y) < 1e-10 && Math.Abs(v.Z) < 1e-10;

        /// <summary>
        /// Determines whether the given <see cref="Vec3"/> is effectively a unit vector (1, 1, 1).
        /// </summary>
        /// <param name="v">The vector to check.</param>
        /// <returns>
        /// True if all components (X, Y, Z) are within 1e-10 of 1; otherwise, false.
        /// </returns>
        /// <remarks>
        /// This method is used to test if a vector is close enough to (1, 1, 1) to be considered as such
        /// for the purposes of FBX scaling property comparisons and resets.
        /// </remarks>
        static bool Is1(Vec3 v) => Math.Abs(v.X - 1) < 1e-10 && Math.Abs(v.Y - 1) < 1e-10 && Math.Abs(v.Z - 1) < 1e-10;

        /// <summary>
        /// Returns the negation of a <see cref="Vec3"/> vector.
        /// </summary>
        /// <param name="v">The vector to negate.</param>
        /// <returns>
        /// A new <see cref="Vec3"/> whose components are the negated values of the input vector.
        /// </returns>
        /// <remarks>
        /// This helper is used to invert the direction of a vector, commonly for pivot or offset calculations
        /// in FBX transform operations.
        /// </remarks>
        static Vec3 Neg(Vec3 v) => new Vec3(-v.X, -v.Y, -v.Z);

        /// <summary>
        /// Returns the string representation of an FBX rotation order code.
        /// </summary>
        /// <param name="o">The integer rotation order code (0-5).</param>
        /// <returns>
        /// A string representing the rotation order, such as "XYZ", "XZY", etc.
        /// If the code is not recognized, returns "?({o})".
        /// </returns>
        /// <remarks>
        /// FBX files encode rotation order as an integer (0-5), which determines the order in which Euler rotations are applied.
        /// This helper method maps those codes to their corresponding string representations for display or debugging purposes.
        /// </remarks>
        static string RN(int o) => o switch { 0 => "XYZ", 1 => "XZY", 2 => "YZX", 3 => "YXZ", 4 => "ZXY", 5 => "ZYX", _ => $"?({o})" };

        /// <summary>
        /// Formats a double-precision floating-point value as a string for FBX output.
        /// </summary>
        /// <param name="v">The double value to format.</param>
        /// <returns>
        /// A string representation of the value, using up to 10 decimal places for typical values,
        /// or scientific notation for very large or very small values. Trailing zeros and decimal points
        /// are trimmed for compactness. Zero is always returned as "0".
        /// </returns>
        /// <remarks>
        /// This method ensures that numbers are formatted in a way that is compatible with FBX ASCII files,
        /// preserving precision while minimizing unnecessary characters.
        /// </remarks>
        static string Fmt(double v)
        {
            if (v == 0) return "0";
            double a = Math.Abs(v);
            if (a >= 0.0001 && a < 1e15)
            {
                string s = v.ToString("F10", CultureInfo.InvariantCulture);
                if (s.Contains('.')) s = s.TrimEnd('0').TrimEnd('.'); return s;
            }
            return v.ToString("G15", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Computes the determinant of the upper-left 3x3 submatrix of a 4x4 matrix.
        /// </summary>
        /// <param name="m">The <see cref="Mat4"/> matrix whose determinant is to be calculated.</param>
        /// <returns>
        /// The determinant of the 3x3 submatrix formed by the first three rows and columns of <paramref name="m"/>.
        /// </returns>
        /// <remarks>
        /// This function is typically used to determine if a transformation matrix includes a mirroring operation
        /// (i.e., a negative determinant indicates a mirrored transformation).
        /// </remarks>
        static double Det3(Mat4 m) =>
            m.M00 * (m.M11 * m.M22 - m.M12 * m.M21) -
            m.M01 * (m.M10 * m.M22 - m.M12 * m.M20) +
            m.M02 * (m.M10 * m.M21 - m.M11 * m.M20);

        /// <summary>
        /// Computes the inverse transpose of the upper-left 3x3 submatrix of a 4x4 matrix,
        /// which is commonly used as the normal transformation matrix in 3D graphics.
        /// </summary>
        /// <param name="m">The <see cref="Mat4"/> matrix whose normal matrix is to be calculated.</param>
        /// <returns>
        /// A <see cref="Mat4"/> representing the inverse transpose of the 3x3 rotation/scaling part of <paramref name="m"/>.
        /// The translation components are set to zero.
        /// </returns>
        /// <remarks>
        /// This function is typically used to correctly transform normal vectors when non-uniform scaling or mirroring
        /// is present in the transformation matrix. The resulting matrix is suitable for transforming normals in FBX geometry processing.
        /// </remarks>
        static Mat4 NormalMat(Mat4 m)
        {
            double a = m.M00, b = m.M01, c = m.M02;
            double d = m.M10, e = m.M11, f = m.M12;
            double g = m.M20, h = m.M21, k = m.M22;
            double det = a * (e * k - f * h) - b * (d * k - f * g) + c * (d * h - e * g);
            double id = 1.0 / det;
            var r = Mat4.Identity;
            r.M00 = (e * k - f * h) * id; r.M01 = -(d * k - f * g) * id; r.M02 = (d * h - e * g) * id;
            r.M10 = -(b * k - c * h) * id; r.M11 = (a * k - c * g) * id; r.M12 = -(a * h - b * g) * id;
            r.M20 = (b * f - c * e) * id; r.M21 = -(a * f - c * d) * id; r.M22 = (a * e - b * d) * id;
            r.M03 = 0; r.M13 = 0; r.M23 = 0;
            return r;
        }

        /// <summary>
        /// Finds the line index of a property in an FBX ASCII file's Properties section.
        /// </summary>
        /// <param name="L">The array of lines representing the FBX file.</param>
        /// <param name="s">The start line index (inclusive) to search from.</param>
        /// <param name="e">The end line index (inclusive) to search to.</param>
        /// <param name="pn">The property name to search for (e.g., "Lcl Rotation").</param>
        /// <returns>
        /// The index of the line containing the property definition (either "P:" or "Property:") with the specified property name in quotes,
        /// or -1 if not found within the specified range.
        /// </returns>
        /// <remarks>
        /// This helper is used to locate the line number of a specific property in the FBX Properties70/Properties60 block,
        /// enabling further reading or writing of property values.
        /// </remarks>
        static int FPL(string[] L, int s, int e, string pn)
        {
            string q = "\"" + pn + "\"";
            for (int i = s; i <= e; i++)
            {
                string t = L[i].TrimStart();
                if ((t.StartsWith("P:") || t.StartsWith("Property:")) && t.Contains(q))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Reads a 3-component vector (Vec3) property from a specified range of lines in an FBX ASCII file.
        /// </summary>
        /// <param name="L">The array of lines representing the FBX file.</param>
        /// <param name="ps">The start line index (inclusive) of the search range.</param>
        /// <param name="pe">The end line index (inclusive) of the search range.</param>
        /// <param name="pn">The property name to search for (e.g., "Lcl Rotation").</param>
        /// <param name="def">The default <see cref="Vec3"/> value to return if the property is not found or cannot be parsed.</param>
        /// <returns>
        /// A <see cref="Vec3"/> containing the parsed property values if found and valid; otherwise, returns <paramref name="def"/>.
        /// </returns>
        /// <remarks>
        /// This method locates the line containing the specified property name within the given range,
        /// parses the last three comma-separated values on that line as doubles, and constructs a <see cref="Vec3"/>.
        /// If the property is not found or parsing fails, the provided default vector is returned.
        /// </remarks>
        static Vec3 RV3(string[] L, int ps, int pe, string pn, Vec3 def)
        {
            int ln = FPL(L, ps, pe, pn); if (ln < 0) return def;
            int col = L[ln].IndexOf(':'); if (col < 0) return def;
            string[] p = CSV(L[ln].Substring(col + 1)); if (p.Length < 3) return def;
            return new Vec3(PD(p[p.Length - 3]), PD(p[p.Length - 2]), PD(p[p.Length - 1]));
        }

        /// <summary>
        /// Writes a 3-component vector (Vec3) property value to a specific property line in an FBX ASCII file.
        /// </summary>
        /// <param name="L">The array of lines representing the FBX file.</param>
        /// <param name="ps">The start line index (inclusive) of the search range.</param>
        /// <param name="pe">The end line index (inclusive) of the search range.</param>
        /// <param name="pn">The property name to search for (e.g., "Lcl Rotation").</param>
        /// <param name="x">The X component value to write.</param>
        /// <param name="y">The Y component value to write.</param>
        /// <param name="z">The Z component value to write.</param>
        /// <remarks>
        /// This method locates the line containing the specified property name within the given range,
        /// then replaces the last three comma-separated values on that line with the provided (x, y, z) values,
        /// formatted appropriately for FBX output. If the property is not found, the method does nothing.
        /// </remarks>
        static void WV3(string[] L, int ps, int pe, string pn, double x, double y, double z)
        {
            int ln = FPL(L, ps, pe, pn); if (ln < 0) return;
            int cf = 0, pos = L[ln].Length - 1;
            while (pos >= 0 && char.IsWhiteSpace(L[ln][pos])) pos--;
            while (pos >= 0) { if (L[ln][pos] == ',') { cf++; if (cf == 3) break; } pos--; }
            if (pos >= 0 && cf >= 3) L[ln] = L[ln].Substring(0, pos + 1) + Fmt(x) + "," + Fmt(y) + "," + Fmt(z);
        }

        /// <summary>
        /// Finds the start and end line indices of a named section in an FBX ASCII file.
        /// </summary>
        /// <param name="L">The array of lines representing the FBX file.</param>
        /// <param name="name">The section name to search for (e.g., "Objects", "Connections").</param>
        /// <param name="s">
        /// Output parameter set to the start line index (inclusive) of the section's content,
        /// or -1 if the section is not found.
        /// </param>
        /// <param name="e">
        /// Output parameter set to the end line index (inclusive) of the section's content,
        /// or -1 if the section is not found.
        /// </param>
        /// <remarks>
        /// This method scans the file for a line starting with the specified section name followed by a colon.
        /// If found, it locates the opening and closing braces that define the section's block,
        /// and sets <paramref name="s"/> and <paramref name="e"/> to the indices of the first and last lines
        /// inside the braces, respectively. If the section or braces are not found, both outputs are set to -1.
        /// </remarks>
        static void FindSec(string[] L, string name, out int s, out int e)
        {
            s = -1;
            e = -1;
            for (int i = 0; i < L.Length; i++)
            {
                string t = L[i].TrimStart();
                if (t.StartsWith(name + ":"))
                {
                    int bo = FB(L, i, L.Length - 1);
                    if (bo < 0) return;
                    int bc = FC(L, bo);
                    if (bc < 0) return;
                    s = bo + 1;
                    e = bc - 1;
                    return;
                }
            }
        }

        /// <summary>
        /// Finds the line index of the first opening brace '{' in the specified range of lines.
        /// </summary>
        /// <param name="L">The array of lines to search.</param>
        /// <param name="f">The starting line index (inclusive) for the search.</param>
        /// <param name="lim">The ending line index (inclusive) for the search.</param>
        /// <returns>
        /// The index of the first line containing an opening brace '{' between <paramref name="f"/> and <paramref name="lim"/> (inclusive).
        /// If a non-empty, non-comment line is encountered after the starting line and before finding a brace, returns -1.
        /// If no opening brace is found in the range, returns -1.
        /// </returns>
        /// <remarks>
        /// This helper is used to locate the start of a block (e.g., section or property group) in an FBX ASCII file.
        /// It skips empty lines and lines starting with a semicolon (';'), which are considered comments.
        /// </remarks>
        static int FB(string[] L, int f, int lim)
        {
            for (int i = f; i <= lim && i < L.Length; i++)
            {
                if (L[i].Contains("{")) return i;
                string t = L[i].TrimStart();
                if (t.Length > 0 && t[0] != ';' && i > f) return -1;
            }
            return -1;
        }

        /// <summary>
        /// Finds the line index of the closing brace '}' that matches the opening brace at the specified line index,
        /// taking into account nested braces and quoted strings.
        /// </summary>
        /// <param name="L">The array of lines to search through.</param>
        /// <param name="ol">The line index where the opening brace '{' is located.</param>
        /// <returns>
        /// The index of the line containing the matching closing brace '}' for the opening brace at <paramref name="ol"/>.
        /// Returns -1 if no matching closing brace is found.
        /// </returns>
        /// <remarks>
        /// This method scans each character in the lines starting from <paramref name="ol"/>. It tracks the nesting depth of braces,
        /// incrementing the depth for each unquoted '{' and decrementing for each unquoted '}'. Quoted braces are ignored.
        /// When the depth returns to zero, the method returns the current line index, indicating the matching closing brace.
        /// </remarks>
        static int FC(string[] L, int ol)
        {
            int d = 0;
            for (int i = ol; i < L.Length; i++)
            {
                string l = L[i];
                bool q = false;
                for (int c = 0; c < l.Length; c++)
                {
                    if (l[c] == '"') q = !q;
                    else if (!q && l[c] == '{') d++;
                    else if (!q && l[c] == '}') { d--;if (d == 0) return i; }
                }
            }
            return -1;
        }


        /// <summary>
        /// Extracts the numeric ID from a line in an FBX ASCII file.
        /// </summary>
        /// <param name="l">The line of text to parse, typically starting with "Model:" or "Geometry:".</param>
        /// <returns>
        /// The parsed long integer ID if found; otherwise, returns 0.
        /// </returns>
        /// <remarks>
        /// This method scans the input line for the first colon, then parses the subsequent characters as a numeric ID.
        /// It skips any trailing 'L' or 'l' characters (common in FBX IDs), and stops parsing at the first non-numeric character.
        /// If the ID cannot be parsed, returns 0.
        /// </remarks>
        static long PId(string l)
        {
            int c = l.IndexOf(':');
            if (c < 0) return 0;
            string a = l.Substring(c + 1).TrimStart();
            var sb = new StringBuilder();
            for (int i = 0; i < a.Length; i++)
            {
                char ch = a[i];
                if (char.IsDigit(ch) || (ch == '-' && sb.Length == 0))
                    sb.Append(ch);
                else if (ch == 'L' || ch == 'l')
                    continue;
                else
                    break;
            }
            return sb.Length > 0 && long.TryParse(sb.ToString(), out long v) ? v : 0;
        }

        /// <summary>
        /// Extracts the model name from a line in an FBX ASCII file.
        /// </summary>
        /// <param name="l">The line of text to parse, typically starting with "Model:" or containing a quoted model name.</param>
        /// <returns>
        /// The extracted model name as a string if found; otherwise, returns "?".
        /// </returns>
        /// <remarks>
        /// This method attempts to extract the model name from a line by searching for the pattern "Model::" within double quotes.
        /// If that pattern is not found, it falls back to extracting the first quoted string in the line.
        /// If no quoted string is found, returns "?".
        /// </remarks>
        static string EName(string l)
        {
            int s = l.IndexOf("\"Model::");
            if (s >= 0)
            {
                s += 8;
                int e = l.IndexOf('"', s);
                if (e > s)
                    return l.Substring(s, e - s);
            }
            s = l.IndexOf('"');
            if (s >= 0)
            {
                int e = l.IndexOf('"', s + 1);
                if (e > s)
                    return l.Substring(s + 1, e - s - 1);
            }
            return "?";
        }

        /// <summary>
        /// Splits a CSV (comma-separated values) string into an array of strings, handling quoted substrings.
        /// </summary>
        /// <param name="s">The input string containing comma-separated values, possibly with quoted sections.</param>
        /// <returns>
        /// An array of strings, each representing a value from the input string. Quoted values containing commas are preserved as single entries.
        /// </returns>
        /// <remarks>
        /// This method parses the input string character by character, tracking whether the current position is inside a quoted substring.
        /// Commas outside of quotes are treated as delimiters, while commas inside quotes are preserved as part of the value.
        /// Leading and trailing whitespace is trimmed from each value. Quoted values retain their quotes in the output.
        /// </remarks>
        static string[] CSV(string s)
        {
            var t = new List<string>();
            var c = new StringBuilder();
            bool q = false;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '"')
                {
                    q = !q;
                    c.Append(ch);
                }
                else if (ch == ',' && !q)
                {
                    string v = c.ToString().Trim();
                    if (v.Length > 0) t.Add(v);
                    c.Clear();
                }
                else c.Append(ch);
            }
            string la = c.ToString().Trim();
            if (la.Length > 0) t.Add(la);
            return t.ToArray();
        }

        /// <summary>
        /// Removes surrounding double quotes from a string, if present.
        /// </summary>
        /// <param name="s">The input string to unquote.</param>
        /// <returns>
        /// The input string without leading and trailing double quotes if both are present; 
        /// otherwise, returns the original string trimmed of whitespace.
        /// </returns>
        /// <remarks>
        /// This helper is used to extract unquoted values from FBX or YAML property strings that may be wrapped in double quotes.
        /// If the string is at least two characters long and both the first and last characters are double quotes, 
        /// the method returns the substring between them. Otherwise, it returns the trimmed string as-is.
        /// </remarks>
        static string UQ(string s)
        {
            s = s.Trim(); return s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"' ? s.Substring(1, s.Length - 2) : s;
        }
        
        /// <summary>
        /// Parses a string representing a long integer, optionally suffixed with 'L' or 'l', and returns its value.
        /// </summary>
        /// <param name="s">The string to parse, which may include whitespace and an optional 'L' or 'l' suffix.</param>
        /// <returns>
        /// The parsed long integer value if successful; otherwise, 0.
        /// </returns>
        /// <remarks>
        /// This method trims whitespace and any trailing 'L' or 'l' characters from the input string,
        /// then attempts to parse it as a long integer using invariant culture and any number style.
        /// If parsing fails, the method returns 0.
        /// </remarks>
        static long PL(string s)
        {
            s = s.Trim().TrimEnd('L', 'l'); return long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out long v) ? v : 0;
        }

        /// <summary>
        /// Parses a string as a double-precision floating-point value using invariant culture.
        /// </summary>
        /// <param name="s">The string to parse as a double.</param>
        /// <returns>
        /// The parsed double value if successful; otherwise, 0.
        /// </returns>
        /// <remarks>
        /// This method trims whitespace from the input string and attempts to parse it as a double using
        /// <see cref="NumberStyles.Float"/> and <see cref="NumberStyles.AllowLeadingSign"/> with invariant culture.
        /// If parsing fails, the method returns 0.
        /// </remarks>
        static double PD(string s)
        {
            return double.TryParse(s.Trim(), NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double v) ? v : 0;
        }


        /// <summary>
        /// Represents a 3D vector with double-precision components.
        /// </summary>
        /// <remarks>
        /// The <c>Vec3</c> struct is used throughout the FBX processing logic to store and manipulate
        /// 3D coordinates, such as positions, rotations, and scales. It provides a simple container
        /// for three double values (X, Y, Z) and a constructor for initialization.
        /// </remarks>
        struct Vec3
        {
            /// <summary>
            /// The X component of the vector.
            /// </summary>
            public double X;

            /// <summary>
            /// The Y component of the vector.
            /// </summary>
            public double Y;

            /// <summary>
            /// The Z component of the vector.
            /// </summary>
            public double Z;

            /// <summary>
            /// Initializes a new instance of the <see cref="Vec3"/> struct with the specified components.
            /// </summary>
            /// <param name="x">The X component.</param>
            /// <param name="y">The Y component.</param>
            /// <param name="z">The Z component.</param>
            public Vec3(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }
        }

        /// <summary>
        /// Represents a 4x4 matrix for 3D transformations, including translation, rotation, and scaling.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The <c>Mat4</c> struct provides methods and operators for constructing and manipulating 4x4 matrices,
        /// which are commonly used in 3D graphics for transforming geometry. It supports identity, translation,
        /// scaling, and Euler rotation matrix creation, as well as matrix multiplication and inversion.
        /// </para>
        /// <para>
        /// The matrix is stored in row-major order, with fields <c>M00</c> through <c>M33</c> representing the elements.
        /// </para>
        /// </remarks>
        struct Mat4
        {
            /// <summary>
            /// Matrix elements in row-major order.
            /// </summary>
            public double M00, M01, M02, M03, M10, M11, M12, M13;
            public double M20, M21, M22, M23, M30, M31, M32, M33;

            /// <summary>
            /// Gets the identity matrix (diagonal elements set to 1, others to 0).
            /// </summary>
            public static Mat4 Identity => new Mat4 { M00 = 1, M11 = 1, M22 = 1, M33 = 1 };

            /// <summary>
            /// Creates a translation matrix from a <see cref="Vec3"/> translation vector.
            /// </summary>
            /// <param name="t">The translation vector.</param>
            /// <returns>A <see cref="Mat4"/> representing the translation.</returns>
            public static Mat4 Tr(Vec3 t)
            {
                var m = Identity; m.M03 = t.X; m.M13 = t.Y; m.M23 = t.Z; return m;
            }

            /// <summary>
            /// Creates a scaling matrix from a <see cref="Vec3"/> scale vector.
            /// </summary>
            /// <param name="s">The scale vector.</param>
            /// <returns>A <see cref="Mat4"/> representing the scaling.</returns>
            public static Mat4 Scale(Vec3 s)
            {
                var m = Identity; m.M00 = s.X; m.M11 = s.Y; m.M22 = s.Z; return m;
            }

            /// <summary>
            /// Creates a rotation matrix for rotation about the X axis.
            /// </summary>
            /// <param name="r">Angle in radians.</param>
            /// <returns>A <see cref="Mat4"/> representing the X-axis rotation.</returns>
            static Mat4 RX(double r)
            {
                double c = Math.Cos(r), s = Math.Sin(r); var m = Identity;
                m.M11 = c; m.M12 = -s; m.M21 = s; m.M22 = c; return m;
            }

            /// <summary>
            /// Creates a rotation matrix for rotation about the Y axis.
            /// </summary>
            /// <param name="r">Angle in radians.</param>
            /// <returns>A <see cref="Mat4"/> representing the Y-axis rotation.</returns>
            static Mat4 RY(double r)
            {
                double c = Math.Cos(r), s = Math.Sin(r);
                var m = Identity; m.M00 = c; m.M02 = s; m.M20 = -s; m.M22 = c; return m;
            }

            /// <summary>
            /// Creates a rotation matrix for rotation about the Z axis.
            /// </summary>
            /// <param name="r">Angle in radians.</param>
            /// <returns>A <see cref="Mat4"/> representing the Z-axis rotation.</returns>
            static Mat4 RZ(double r)
            {
                double c = Math.Cos(r), s = Math.Sin(r);
                var m = Identity; m.M00 = c; m.M01 = -s; m.M10 = s; m.M11 = c; return m;
            }

            /// <summary>
            /// Constructs a rotation matrix from Euler angles and a specified rotation order.
            /// </summary>
            /// <param name="d">Euler angles in degrees (X, Y, Z).</param>
            /// <param name="o">Rotation order (0-5, matching FBX convention).</param>
            /// <returns>A <see cref="Mat4"/> representing the Euler rotation.</returns>
            public static Mat4 Euler(Vec3 d, int o)
            {
                Mat4 X = RX(d.X * Math.PI / 180), Y = RY(d.Y * Math.PI / 180), Z = RZ(d.Z * Math.PI / 180);
                return o switch { 0 => Z * Y * X, 1 => Y * Z * X, 2 => X * Z * Y, 3 => Z * X * Y, 4 => Y * X * Z, 5 => X * Y * Z, _ => Z * Y * X };
            }

            /// <summary>
            /// Multiplies two <see cref="Mat4"/> matrices.
            /// </summary>
            /// <param name="a">The left matrix.</param>
            /// <param name="b">The right matrix.</param>
            /// <returns>The product of <paramref name="a"/> and <paramref name="b"/>.</returns>
            public static Mat4 operator *(Mat4 a, Mat4 b)
            {
                var r = new Mat4();
                r.M00 = a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20 + a.M03 * b.M30; r.M01 = a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21 + a.M03 * b.M31;
                r.M02 = a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22 + a.M03 * b.M32; r.M03 = a.M00 * b.M03 + a.M01 * b.M13 + a.M02 * b.M23 + a.M03 * b.M33;
                r.M10 = a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20 + a.M13 * b.M30; r.M11 = a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31;
                r.M12 = a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32; r.M13 = a.M10 * b.M03 + a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33;
                r.M20 = a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20 + a.M23 * b.M30; r.M21 = a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31;
                r.M22 = a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32; r.M23 = a.M20 * b.M03 + a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33;
                r.M30 = a.M30 * b.M00 + a.M31 * b.M10 + a.M32 * b.M20 + a.M33 * b.M30; r.M31 = a.M30 * b.M01 + a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31;
                r.M32 = a.M30 * b.M02 + a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32; r.M33 = a.M30 * b.M03 + a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33;
                return r;
            }

            /// <summary>
            /// Computes the inverse of a 4x4 matrix.
            /// </summary>
            /// <param name="m">The matrix to invert.</param>
            /// <returns>The inverse of <paramref name="m"/>.</returns>
            /// <exception cref="InvalidOperationException">Thrown if the matrix is singular (non-invertible).</exception>
            public static Mat4 Invert(Mat4 m)
            {
                double a00 = m.M00, a01 = m.M01, a02 = m.M02, a03 = m.M03, a10 = m.M10, a11 = m.M11, a12 = m.M12, a13 = m.M13;
                double a20 = m.M20, a21 = m.M21, a22 = m.M22, a23 = m.M23, a30 = m.M30, a31 = m.M31, a32 = m.M32, a33 = m.M33;
                double b00 = a00 * a11 - a01 * a10, b01 = a00 * a12 - a02 * a10, b02 = a00 * a13 - a03 * a10;
                double b03 = a01 * a12 - a02 * a11, b04 = a01 * a13 - a03 * a11, b05 = a02 * a13 - a03 * a12;
                double b06 = a20 * a31 - a21 * a30, b07 = a20 * a32 - a22 * a30, b08 = a20 * a33 - a23 * a30;
                double b09 = a21 * a32 - a22 * a31, b10 = a21 * a33 - a23 * a31, b11 = a22 * a33 - a23 * a32;
                double det = b00 * b11 - b01 * b10 + b02 * b09 + b03 * b08 - b04 * b07 + b05 * b06;
                if (Math.Abs(det) < 1e-14) throw new InvalidOperationException("Singular matrix.");
                double iv = 1.0 / det; var r = new Mat4();
                r.M00 = (a11 * b11 - a12 * b10 + a13 * b09) * iv; r.M01 = (-a01 * b11 + a02 * b10 - a03 * b09) * iv;
                r.M02 = (a31 * b05 - a32 * b04 + a33 * b03) * iv; r.M03 = (-a21 * b05 + a22 * b04 - a23 * b03) * iv;
                r.M10 = (-a10 * b11 + a12 * b08 - a13 * b07) * iv; r.M11 = (a00 * b11 - a02 * b08 + a03 * b07) * iv;
                r.M12 = (-a30 * b05 + a32 * b02 - a33 * b01) * iv; r.M13 = (a20 * b05 - a22 * b02 + a23 * b01) * iv;
                r.M20 = (a10 * b10 - a11 * b08 + a13 * b06) * iv; r.M21 = (-a00 * b10 + a01 * b08 - a03 * b06) * iv;
                r.M22 = (a30 * b04 - a31 * b02 + a33 * b00) * iv; r.M23 = (-a20 * b04 + a21 * b02 - a23 * b00) * iv;
                r.M30 = (-a10 * b09 + a11 * b07 - a12 * b06) * iv; r.M31 = (a00 * b09 - a01 * b07 + a02 * b06) * iv;
                r.M32 = (-a30 * b03 + a31 * b01 - a32 * b00) * iv; r.M33 = (a20 * b03 - a21 * b01 + a22 * b00) * iv;
                return r;
            }
        }
    }
}