#Requires -Version 7.2
<#
.SYNOPSIS
  Deterministic file inventory of an artifacts-path output tree, with raw and normalised SHA-256 hashes.
.DESCRIPTION
  Inventory scope: every file under <ArtifactsPath>\bin (the build output folders; obj is intermediate and excluded).
  Order: ordinal sort of the path relative to <ArtifactsPath>, with '/' separators.
  Raw hash: SHA-256 of the file bytes.
  Normalised hash (managed PE files only, .dll/.exe with CLI metadata): SHA-256 of the bytes after zeroing exactly the
  fields that embed the build location or are derived from content hashes that include it:
    - the COFF header TimeDateStamp (a deterministic-build hash value, not a clock time);
    - the TimeDateStamp of every debug-directory entry;
    - the data of the CodeView (type 2) and PdbChecksum (type 19) debug entries (PDB path, PDB id, PDB checksum);
    - the module's MVID (a content hash in deterministic builds), which must occur exactly once in the file.
  Nothing else is changed: all IL, metadata, resources and strings take part in the normalised hash.
  Native executables (apphosts) have no CLI metadata and only get the raw hash.
  Columns: Path, Length, Sha256, NormalizedSha256, Mvid, PdbPath, Kind
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArtifactsPath,
    [Parameter(Mandatory)][string]$OutFile,
    [int]$Throttle = 6,
    [switch]$MaskRegexGeneratorHash                 # diagnostic: see MaskRegexGeneratorHash in the C# below
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not ('AsmNorm' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

public sealed class AsmNormResult { public string Raw, Norm, Mvid, Pdb, Kind; }

public static class AsmNorm
{
    static string Hex(byte[] b) { return BitConverter.ToString(SHA256.HashData(b)).Replace("-", ""); }

    public static AsmNormResult Run(byte[] bytes)
    {
        var r = new AsmNormResult { Raw = Hex(bytes), Norm = "", Mvid = "", Pdb = "", Kind = "file" };
        return r;
    }

    // Diagnostic option: zero the hex digits that follow the ASCII text "RegexGenerator_g" (the Regex source generator
    // names its helper classes with a hash). Used only to test whether those names explain a normalised difference.
    // Generalised: the C# compiler names a file-local type "<FileName>F<64 hex digits>__TypeName"; the digits are a SHA-256 of the
    // source file's path. For generated source that path lies under the build's obj folder, so it changes with the artifacts path.
    // Pattern masked: '>' 'F' <64 upper-case hex digits> '_' '_'.
    static bool IsHex(byte c) { return (c >= (byte)'0' && c <= (byte)'9') || (c >= (byte)'A' && c <= (byte)'F'); }
    static void MaskRegexGeneratorHash(byte[] b)
    {
        for (int i = 0; i + 69 < b.Length; i++)
        {
            if (b[i] != (byte)'>' || b[i + 1] != (byte)'F') continue;
            bool hex = true;
            for (int k = 0; k < 64; k++) if (!IsHex(b[i + 2 + k])) { hex = false; break; }
            if (!hex || b[i + 66] != (byte)'_' || b[i + 67] != (byte)'_') continue;
            for (int k = 0; k < 64; k++) b[i + 2 + k] = (byte)'0';
        }
    }

    public static AsmNormResult RunPe(byte[] bytes) { return RunPe(bytes, false); }

    public static AsmNormResult RunPe(byte[] bytes, bool maskRegex)
    {
        var r = new AsmNormResult { Raw = Hex(bytes), Norm = "", Mvid = "", Pdb = "", Kind = "pe-native" };
        try
        {
            using (var ms = new MemoryStream(bytes, false))
            using (var pe = new PEReader(ms))
            {
                if (!pe.HasMetadata) return r;
                r.Kind = "pe-managed";
                var copy = (byte[])bytes.Clone();
                if (maskRegex) MaskRegexGeneratorHash(copy);
                var hdrs = pe.PEHeaders;
                int o = hdrs.CoffHeaderStartOffset + 4;
                for (int i = 0; i < 4; i++) copy[o + i] = 0;
                var dd = hdrs.PEHeader.DebugTableDirectory;
                int tableOffset;
                if (dd.Size > 0 && hdrs.TryGetDirectoryOffset(dd, out tableOffset))
                {
                    var entries = pe.ReadDebugDirectory();
                    for (int e = 0; e < entries.Length; e++)
                    {
                        int eo = tableOffset + 28 * e;
                        for (int i = 0; i < 4; i++) copy[eo + 4 + i] = 0;
                        var ent = entries[e];
                        if (ent.Type == DebugDirectoryEntryType.CodeView || (int)ent.Type == 19)
                            for (int i = 0; i < ent.DataSize; i++) copy[ent.DataPointer + i] = 0;
                        if (ent.Type == DebugDirectoryEntryType.CodeView)
                            r.Pdb = pe.ReadCodeViewDebugDirectoryData(ent).Path;
                    }
                }
                var md = pe.GetMetadataReader();
                Guid g = md.GetGuid(md.GetModuleDefinition().Mvid);
                r.Mvid = g.ToString();
                byte[] needle = g.ToByteArray();
                int hits = 0, at = -1;
                for (int i = 0; i <= copy.Length - 16; i++)
                {
                    if (copy[i] != needle[0]) continue;
                    bool m = true;
                    for (int j = 1; j < 16; j++) if (copy[i + j] != needle[j]) { m = false; break; }
                    if (m) { hits++; at = i; }
                }
                if (hits == 1) for (int j = 0; j < 16; j++) copy[at + j] = 0;
                else { r.Kind = "pe-managed-mvid-hits-" + hits; return r; }
                r.Norm = Hex(copy);
            }
        }
        catch (Exception ex) { r.Kind = "pe-error: " + ex.Message; }
        return r;
    }
}
'@
}

$root = (Resolve-Path -LiteralPath $ArtifactsPath).Path.TrimEnd('\')
$bin = Join-Path $root 'bin'
if (-not (Test-Path -LiteralPath $bin)) { throw "No bin folder under $root" }

$files = Get-ChildItem -LiteralPath $bin -Recurse -File -Force
$rows = $files | ForEach-Object -ThrottleLimit $Throttle -Parallel {
    $f = $_; $root = $using:root; $mask = [bool]$using:MaskRegexGeneratorHash
    $rel = $f.FullName.Substring($root.Length + 1).Replace('\', '/')
    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    $ext = $f.Extension.ToLowerInvariant()
    $r = if ($ext -eq '.dll' -or $ext -eq '.exe') { [AsmNorm]::RunPe($bytes, $mask) } else { [AsmNorm]::Run($bytes) }
    [pscustomobject]@{ Path = $rel; Length = $bytes.Length; Sha256 = $r.Raw; NormalizedSha256 = $r.Norm; Mvid = $r.Mvid; PdbPath = ($r.Pdb -replace [regex]::Escape($root), '<ART>'); Kind = $r.Kind }
}
$sorted = [System.Collections.Generic.List[object]]::new(@($rows))
$sorted.Sort([System.Comparison[object]] { param($a, $b) [string]::CompareOrdinal($a.Path, $b.Path) })
$sorted | Export-Csv -LiteralPath $OutFile -NoTypeInformation -Encoding utf8
"inventory: $(@($rows).Count) files -> $OutFile"
