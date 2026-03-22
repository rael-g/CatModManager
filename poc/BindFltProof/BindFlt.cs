using System;
using System.Runtime.InteropServices;

namespace BindFltProof;

/// <summary>
/// P/Invoke bindings for bindflt.dll (Windows Bind Filter Driver).
///
/// bindflt.sys is a kernel filter driver shipping since Windows 10 1903 (Build 18362).
/// It is used by WSL2, Windows Sandbox, Windows Container Isolation and Dev Drive.
///
/// The API is undocumented but stable — it has not changed since its introduction.
/// Signatures were sourced from:
///   • ReactOS (public research)
///   • Windows Driver Kit headers (bindfilter.h, available in WDK 10)
///   • Reverse-engineering of bindflt.dll
///
/// Key semantics of BINDFLT_FLAG_MERGED_BIND_MAPPING:
///   Without merge : VirtRootPath is replaced by RealRootPath (opaque redirect)
///   With    merge : RealRootPath is overlaid ON TOP of VirtRootPath.
///                   Files in RealRootPath shadow same-named files in VirtRootPath.
///                   Files absent from RealRootPath fall through to VirtRootPath on disk.
///   This gives us a mod-overlay with zero file copying and zero SafeSwap.
///
/// Privilege requirement:
///   BfSetupFilter requires SeCreateSymbolicLinkPrivilege or Administrator.
///   In practice, running the host process as Administrator is sufficient.
/// </summary>
internal static class BindFlt
{
    // ── Flags ────────────────────────────────────────────────────────────────

    /// <summary>Mapping is read-only from the caller's perspective.</summary>
    public const uint BINDFLT_FLAG_READ_ONLY_MAPPING     = 0x00000002;

    /// <summary>
    /// Overlay (merge) RealRootPath ON TOP of VirtRootPath.
    /// Files present in RealRootPath shadow corresponding files under VirtRootPath.
    /// Files absent from RealRootPath are served from the real disk at VirtRootPath.
    /// </summary>
    public const uint BINDFLT_FLAG_MERGED_BIND_MAPPING   = 0x00000004;

    /// <summary>
    /// Mapping applies to the current process's silo (session) rather than globally.
    /// Use this flag to restrict the filter to the current process tree.
    /// Without it the mapping is kernel-global and affects all processes.
    /// </summary>
    public const uint BINDFLT_FLAG_USE_CURRENT_SILO_MAPPING = 0x00000010;

    // ── Imports ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Establishes a bind-filter mapping.
    /// </summary>
    /// <param name="jobHandle">
    ///   Optional handle to a Job object that scopes the mapping.
    ///   Pass IntPtr.Zero for a session-wide (current user session) mapping.
    /// </param>
    /// <param name="flags">Combination of BINDFLT_FLAG_* constants.</param>
    /// <param name="virtRootPath">
    ///   The virtual path that processes will access.
    ///   For mod overlay this is the actual game folder path.
    /// </param>
    /// <param name="realRootPath">
    ///   The real path that backs the virtual root.
    ///   For mod overlay this is the mod staging directory.
    /// </param>
    /// <param name="exceptionPaths">
    ///   Array of paths under VirtRootPath that are excluded from the mapping.
    ///   Pass null to include everything.
    /// </param>
    /// <param name="exceptionPathCount">Number of entries in exceptionPaths.</param>
    /// <returns>HRESULT — S_OK (0) on success.</returns>
    [DllImport("bindflt.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int BfSetupFilter(
        IntPtr   jobHandle,
        uint     flags,
        string   virtRootPath,
        string   realRootPath,
        string[]? exceptionPaths,
        uint     exceptionPathCount);

    /// <summary>
    /// Removes a previously established mapping.
    /// </summary>
    [DllImport("bindflt.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int BfRemoveMapping(
        IntPtr jobHandle,
        string virtRootPath);

    // ── Helpers ──────────────────────────────────────────────────────────────

    public static void ThrowIfFailed(int hr, string context)
    {
        if (hr != 0)
            throw new ExternalException($"{context} failed with HRESULT 0x{hr:X8}", hr);
    }

    /// <summary>
    /// Returns true if bindflt.dll is loadable on this system (Win10 1903+).
    /// </summary>
    public static bool IsAvailable()
    {
        try
        {
            // Quick test call with invalid args — if bindflt.dll is present it
            // will return an error code rather than throw DllNotFoundException.
            BfSetupFilter(IntPtr.Zero, 0, "x", "x", null, 0);
            return true;
        }
        catch (DllNotFoundException)  { return false; }
        catch                         { return true;  } // any other error = dll present
    }
}
