// FBX ASCII Rebase Rotation Tool
// by: vector_cmdr (https://github.com/vectorcmdr)
// 
// This tool processes ASCII FBX files in the current directory, identifies "Lcl Rotation"
// properties within "Model: \"Mesh\"" sections, and moves their values to new "GeometricRotation" properties.
// It creates new files with "_fixed" appended to the original filename, preserving the original files.
//
// It also processes Unity Prefab files and updates the Transform: m_LocalRotation: and m_LocalScale: properties
// to match the changes made in the FBX files, ensuring consistency between the model and prefab data.
//
// Usage: Place this executable in the same directory as your .fbx and or .prefab files and run it.
// It will process all .fbx and .prefab files and output fixed versions.
// License: MIT License (https://opensource.org/licenses/MIT)

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

class Program
{
    static void Main(string[] args)
    {
        string directory = AppContext.BaseDirectory;

        string[] fbxFiles = Directory.GetFiles(directory, "*.fbx");
        string[] prefabFiles = Directory.GetFiles(directory, "*.prefab");

        if (fbxFiles.Length == 0 && prefabFiles.Length == 0)
        {
            Console.WriteLine("No .fbx or .prefab files found in the current directory.");
            return;
        }

        // Process FBX files
        foreach (string filePath in fbxFiles)
        {
            if (Path.GetFileNameWithoutExtension(filePath).EndsWith("_fixed", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Skipping already-fixed file: {Path.GetFileName(filePath)}");
                continue;
            }

            Console.WriteLine($"Processing FBX: {Path.GetFileName(filePath)}");

            if (!IsAsciiFbx(filePath))
            {
                Console.WriteLine($"  Skipped: '{Path.GetFileName(filePath)}' is binary FBX, not ASCII.");
                continue;
            }

            ProcessFbxFile(filePath);
        }

        // Process Prefab files
        foreach (string filePath in prefabFiles)
        {
            if (Path.GetFileNameWithoutExtension(filePath).EndsWith("_fixed", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Skipping already-fixed file: {Path.GetFileName(filePath)}");
                continue;
            }

            Console.WriteLine($"Processing Prefab: {Path.GetFileName(filePath)}");
            ProcessPrefabFile(filePath);
        }

        Console.WriteLine("\nDone. Press any key to exit.");
        Console.ReadKey();
    }

    // =========================================================================
    //  FBX Processing
    // =========================================================================

    static void ProcessFbxFile(string filePath)
    {
        try
        {
            List<string> lines = new List<string>(File.ReadAllLines(filePath));
            int rotationCount = 0;
            int scalingCount = 0;

            bool inModelSection = false;
            bool inProperties70 = false;
            int braceDepthModel = 0;
            int braceDepthProperties = 0;
            int properties70StartIndex = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();

                if (!inModelSection && trimmed.StartsWith("Model:") && trimmed.Contains("\"Mesh\""))
                {
                    inModelSection = true;
                    braceDepthModel = 0;
                    braceDepthModel += CountChar(line, '{') - CountChar(line, '}');
                    continue;
                }

                if (inModelSection)
                {
                    braceDepthModel += CountChar(line, '{') - CountChar(line, '}');

                    if (!inProperties70 && trimmed.StartsWith("Properties70:"))
                    {
                        inProperties70 = true;
                        braceDepthProperties = 0;
                        braceDepthProperties += CountChar(line, '{') - CountChar(line, '}');
                        properties70StartIndex = i;
                        continue;
                    }

                    if (inProperties70)
                    {
                        braceDepthProperties += CountChar(line, '{') - CountChar(line, '}');

                        if (trimmed.StartsWith("P:") && trimmed.Contains("\"Lcl Rotation\""))
                        {
                            if (ProcessLclProperty(lines, ref i, properties70StartIndex,
                                    "Lcl Rotation", "GeometricRotation", "0,0,0"))
                            {
                                rotationCount++;
                            }
                        }
                        else if (trimmed.StartsWith("P:") && trimmed.Contains("\"Lcl Scaling\""))
                        {
                            if (ProcessLclProperty(lines, ref i, properties70StartIndex,
                                    "Lcl Scaling", "GeometricScaling", "1,1,1"))
                            {
                                scalingCount++;
                            }
                        }

                        if (braceDepthProperties <= 0)
                        {
                            inProperties70 = false;
                            properties70StartIndex = -1;
                        }
                    }

                    if (braceDepthModel <= 0)
                    {
                        inModelSection = false;
                        inProperties70 = false;
                        properties70StartIndex = -1;
                    }
                }
            }

            int totalModifications = rotationCount + scalingCount;

            if (totalModifications > 0)
            {
                string outputFileName = Path.GetFileNameWithoutExtension(filePath) + "_fixed.fbx";
                string outputPath = Path.Combine(Path.GetDirectoryName(filePath)!, outputFileName);
                File.WriteAllLines(outputPath, lines);
                Console.WriteLine($"  {rotationCount} rotation(s) and {scalingCount} scaling(s) moved to Geometric properties. Saved: {outputFileName}");
            }
            else
            {
                Console.WriteLine("  No 'Lcl Rotation' or 'Lcl Scaling' entries found to modify.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error processing FBX file: {ex.Message}");
        }
    }

    static bool ProcessLclProperty(List<string> lines, ref int currentIndex, int properties70StartIndex,
        string lclName, string geometricName, string defaultValues)
    {
        string line = lines[currentIndex];
        string trimmed = line.TrimStart();

        string escapedName = Regex.Escape(lclName);
        string pattern = $@"^P:\s*""{escapedName}""\s*,\s*""{escapedName}""\s*,\s*""[^""]*""\s*,\s*""([^""]*)""\s*,\s*([^,]+)\s*,\s*([^,]+)\s*,\s*(.+)$";

        Match match = Regex.Match(trimmed, pattern);
        if (!match.Success)
            return false;

        string aValue = match.Groups[1].Value;
        string x = match.Groups[2].Value.Trim();
        string y = match.Groups[3].Value.Trim();
        string z = match.Groups[4].Value.Trim();

        string indent = line.Substring(0, line.Length - trimmed.Length);

        // Search backwards for an existing Geometric line in this Properties70 block
        int existingGeoIndex = -1;
        for (int j = currentIndex - 1; j > properties70StartIndex; j--)
        {
            string checkTrimmed = lines[j].TrimStart();
            if (checkTrimmed.StartsWith("P:") && checkTrimmed.Contains($"\"{geometricName}\""))
            {
                existingGeoIndex = j;
                break;
            }
        }

        // Also search forwards in case the Geometric line is below the Lcl line
        if (existingGeoIndex < 0)
        {
            for (int j = currentIndex + 1; j < lines.Count; j++)
            {
                string checkTrimmed = lines[j].TrimStart();
                if (checkTrimmed.StartsWith("}"))
                    break;
                if (checkTrimmed.StartsWith("P:") && checkTrimmed.Contains($"\"{geometricName}\""))
                {
                    existingGeoIndex = j;
                    break;
                }
            }
        }

        if (existingGeoIndex >= 0)
        {
            string geoIndent = lines[existingGeoIndex].Substring(0,
                lines[existingGeoIndex].Length - lines[existingGeoIndex].TrimStart().Length);
            lines[existingGeoIndex] = $"{geoIndent}P: \"{geometricName}\", \"Vector3D\", \"Vector\", \"\",{x},{y},{z}";
        }
        else
        {
            string geometricLine = $"{indent}P: \"{geometricName}\", \"Vector3D\", \"Vector\", \"\",{x},{y},{z}";
            lines.Insert(currentIndex, geometricLine);
            currentIndex++;
        }

        lines[currentIndex] = $"{indent}P: \"{lclName}\", \"{lclName}\", \"\", \"{aValue}\",{defaultValues}";

        return true;
    }

    // =========================================================================
    //  Prefab (Unity YAML) Processing
    // =========================================================================

    static void ProcessPrefabFile(string filePath)
    {
        try
        {
            List<string> lines = new List<string>(File.ReadAllLines(filePath));
            int rotationCount = 0;
            int scalingCount = 0;

            bool inTransform = false;
            int transformIndent = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];

                // Skip empty lines without changing state
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                int currentIndent = GetYamlIndent(line);
                string trimmed = line.TrimStart();

                // Unity YAML document separators (--- !u!...) reset context
                if (trimmed.StartsWith("---"))
                {
                    inTransform = false;
                    transformIndent = -1;
                    continue;
                }

                // Detect a Transform component block.
                // In Unity prefabs this appears as a top-level key like "Transform:" or
                // as part of a stripped document "--- !u!4 &xxxx" followed by properties.
                // The component type line looks like: "  m_ObjectHideFlags: ..." after a
                // "--- !u!4" header OR explicitly "Transform:".
                // We handle both by detecting the "--- !u!4" document header (Transform
                // component class ID) or an explicit "Transform:" key.

                if (trimmed.StartsWith("--- !u!4 ") || trimmed.StartsWith("--- !u!4&"))
                {
                    // This is a Transform component document
                    inTransform = true;
                    transformIndent = 0; // Properties will be at indent > 0
                    continue;
                }

                // Also catch explicit "Transform:" mapping key (less common but possible)
                if (trimmed == "Transform:" || trimmed.StartsWith("Transform:"))
                {
                    inTransform = true;
                    transformIndent = currentIndent;
                    continue;
                }

                if (inTransform)
                {
                    // If we hit a new document separator or a line at the same/lower indent
                    // that is a different top-level key, we've left the Transform block.
                    // (Document separators are already handled above.)

                    // Detect m_LocalRotation
                    if (trimmed.StartsWith("m_LocalRotation:"))
                    {
                        // Value can be inline: m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
                        // or on subsequent indented lines (flow vs block style).
                        if (trimmed.Contains("{"))
                        {
                            // Inline flow mapping — replace the whole value
                            string indent = line.Substring(0, currentIndent);
                            lines[i] = $"{indent}m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}";
                            rotationCount++;
                        }
                        else
                        {
                            // Block-style mapping on subsequent lines:
                            //   m_LocalRotation:
                            //     x: 0
                            //     y: 0
                            //     z: 0
                            //     w: 1
                            // Replace with inline form
                            string indent = line.Substring(0, currentIndent);
                            lines[i] = $"{indent}m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}";

                            // Remove subsequent indented child lines (x:, y:, z:, w:)
                            while (i + 1 < lines.Count)
                            {
                                string nextTrimmed = lines[i + 1].TrimStart();
                                int nextIndent = GetYamlIndent(lines[i + 1]);
                                if (nextIndent > currentIndent &&
                                    (nextTrimmed.StartsWith("x:") || nextTrimmed.StartsWith("y:") ||
                                     nextTrimmed.StartsWith("z:") || nextTrimmed.StartsWith("w:")))
                                {
                                    lines.RemoveAt(i + 1);
                                }
                                else
                                {
                                    break;
                                }
                            }

                            rotationCount++;
                        }
                        continue;
                    }

                    // Detect m_LocalScale
                    if (trimmed.StartsWith("m_LocalScale:"))
                    {
                        if (trimmed.Contains("{"))
                        {
                            string indent = line.Substring(0, currentIndent);
                            lines[i] = $"{indent}m_LocalScale: {{x: 1, y: 1, z: 1}}";
                            scalingCount++;
                        }
                        else
                        {
                            string indent = line.Substring(0, currentIndent);
                            lines[i] = $"{indent}m_LocalScale: {{x: 1, y: 1, z: 1}}";

                            while (i + 1 < lines.Count)
                            {
                                string nextTrimmed = lines[i + 1].TrimStart();
                                int nextIndent = GetYamlIndent(lines[i + 1]);
                                if (nextIndent > currentIndent &&
                                    (nextTrimmed.StartsWith("x:") || nextTrimmed.StartsWith("y:") ||
                                     nextTrimmed.StartsWith("z:")))
                                {
                                    lines.RemoveAt(i + 1);
                                }
                                else
                                {
                                    break;
                                }
                            }

                            scalingCount++;
                        }
                        continue;
                    }
                }
            }

            int totalModifications = rotationCount + scalingCount;

            if (totalModifications > 0)
            {
                string outputFileName = Path.GetFileNameWithoutExtension(filePath) + "_fixed.prefab";
                string outputPath = Path.Combine(Path.GetDirectoryName(filePath)!, outputFileName);
                File.WriteAllLines(outputPath, lines);
                Console.WriteLine($"  {rotationCount} rotation(s) and {scalingCount} scaling(s) reset in Transform components. Saved: {outputFileName}");
            }
            else
            {
                Console.WriteLine("  No Transform m_LocalRotation or m_LocalScale entries found to modify.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Error processing Prefab file: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the number of leading spaces for a YAML line (indent level).
    /// </summary>
    static int GetYamlIndent(string line)
    {
        int count = 0;
        foreach (char c in line)
        {
            if (c == ' ') count++;
            else break;
        }
        return count;
    }

    // =========================================================================
    //  Shared Utilities
    // =========================================================================

    /// <summary>
    /// Checks whether an FBX file is ASCII format.
    /// Binary FBX files start with the magic bytes "Kaydara FBX Binary  \0".
    /// </summary>
    static bool IsAsciiFbx(string filePath)
    {
        try
        {
            byte[] header = new byte[23];
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                int bytesRead = fs.Read(header, 0, header.Length);
                if (bytesRead < header.Length)
                    return false;
            }

            string binaryMagic = "Kaydara FBX Binary";
            string headerStr = System.Text.Encoding.ASCII.GetString(header, 0, binaryMagic.Length);

            return !headerStr.Equals(binaryMagic, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    static int CountChar(string s, char c)
    {
        int count = 0;
        foreach (char ch in s)
        {
            if (ch == c) count++;
        }
        return count;
    }
}