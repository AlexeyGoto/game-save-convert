using MandarinJuiceCore.GameProfile;
using MandarinJuiceCore.Helpers;
using MandarinJuiceCore.Models.DSSS.Mandarin;

namespace SaveConvert;

/// <summary>
/// Decrypt / Encrypt / Re-sign save files via MandarinJuiceCore (no CLI).
/// </summary>
static class SaveOperations
{
    /// <summary>
    /// Attempts to decrypt the file data with the given userId.
    /// Returns true if decryption succeeds (data belongs to that user).
    /// </summary>
    public static bool TryDecrypt(byte[] fileData, MandarinGameProfile profile, ulong userId)
    {
        try
        {
            var de = new MandarinDeencryptor(profile.MandarinSeed);
            var mf = new MandarinFile(de, profile.MandarinFileFlavor);
            mf.SetFileData(fileData, encryptedFilesOnly: true);
            if (!mf.IsEncrypted) return false;
            mf.DecryptFile(userId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Decrypts file and reads container version from offset 0x28.
    /// Returns (version, buildVersion) or null if decryption fails.
    /// </summary>
    public static (uint version, uint build)? ReadSaveVersion(byte[] fileData, MandarinGameProfile profile, ulong userId, bool isData001 = false)
    {
        try
        {
            var de = new MandarinDeencryptor(profile.MandarinSeed);
            var mf = new MandarinFile(de, profile.MandarinFileFlavor);
            mf.SetFileData(fileData);
            if (mf.IsEncrypted) mf.DecryptFile(userId);
            byte[] raw = mf.Data;
            if (raw.Length < 0x60) return null;
            uint version = BitConverter.ToUInt32(raw, 0x28);
            // Build version: at 0x5C for data000/slot, at 0x4C for data00-1
            uint build = BitConverter.ToUInt32(raw, isData001 ? 0x4C : 0x5C);
            return (version, build);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Re-signs a single file: decrypt with sourceUserId, encrypt with targetUserId.
    /// Returns (newFileBytes, skipped). If the file is already encrypted for targetUserId, returns (original, true).
    /// </summary>
    public static (byte[] data, bool skipped) ResignFile(byte[] fileData, MandarinGameProfile profile, ulong sourceUserId, ulong targetUserId)
    {
        var de = new MandarinDeencryptor(profile.MandarinSeed);
        var mf = new MandarinFile(de, profile.MandarinFileFlavor);
        mf.SetFileData(fileData);
        if (mf.IsEncrypted)
        {
            try
            {
                mf.DecryptFile(sourceUserId);
            }
            catch
            {
                // Source ID failed — check if already encrypted for target
                if (TryDecrypt(fileData, profile, targetUserId))
                    return (fileData, true);
                throw; // Neither source nor target — real error
            }
        }
        mf.EncryptFile(targetUserId);
        return (mf.GetFileData(), false);
    }

    /// <summary>
    /// Re-sign + optional BUILD patch. For data000 and slot files.
    /// </summary>
    public static (byte[] data, bool skipped, bool buildPatched) ResignFileWithPatch(
        byte[] fileData, MandarinGameProfile profile,
        ulong sourceUserId, ulong targetUserId,
        bool isData001, uint? targetBuild = null)
    {
        var de = new MandarinDeencryptor(profile.MandarinSeed);
        var mf = new MandarinFile(de, profile.MandarinFileFlavor);
        mf.SetFileData(fileData);
        if (mf.IsEncrypted)
        {
            try { mf.DecryptFile(sourceUserId); }
            catch
            {
                if (TryDecrypt(fileData, profile, targetUserId))
                    return (fileData, true, false);
                throw;
            }
        }

        bool buildPatched = false;
        byte[] raw = mf.Data;
        if (targetBuild != null && raw.Length >= 0x60)
        {
            int offset = isData001 ? SavePatching.BuildOffsetData001 : SavePatching.BuildOffsetSlot;
            uint curBuild = BitConverter.ToUInt32(raw, offset);
            if (curBuild > targetBuild.Value)
            {
                BitConverter.GetBytes(targetBuild.Value).CopyTo(raw, offset);
                buildPatched = true;
            }
        }

        mf.EncryptFile(targetUserId);
        return (mf.GetFileData(), false, buildPatched);
    }

    /// <summary>
    /// Process data00-1.bin: re-sign + optional BUILD patch + VERSION+2 (on downgrade).
    /// </summary>
    public static (byte[] data, uint oldVer, uint newVer, bool buildPatched) ProcessData001(
        byte[] fileData, MandarinGameProfile profile,
        ulong sourceUserId, ulong targetUserId,
        uint? targetBuild = null, bool patchVersion = false)
    {
        var de = new MandarinDeencryptor(profile.MandarinSeed);
        var mf = new MandarinFile(de, profile.MandarinFileFlavor);
        mf.SetFileData(fileData);
        if (mf.IsEncrypted) mf.DecryptFile(sourceUserId);

        byte[] raw = mf.Data;
        uint oldVer = raw.Length >= 0x2C ? BitConverter.ToUInt32(raw, 0x28) : 0;
        uint newVer = oldVer;
        bool buildPatched = false;

        if (raw.Length >= 0x60)
        {
            // BUILD patch (downgrade only)
            if (targetBuild != null)
            {
                uint curBuild = BitConverter.ToUInt32(raw, SavePatching.BuildOffsetData001);
                if (curBuild > targetBuild.Value)
                {
                    BitConverter.GetBytes(targetBuild.Value).CopyTo(raw, SavePatching.BuildOffsetData001);
                    buildPatched = true;
                }
            }

            // VERSION+2 (only if build was actually downgraded)
            if (patchVersion && buildPatched)
            {
                newVer = oldVer + 2;
                BitConverter.GetBytes(newVer).CopyTo(raw, 0x28);
            }
        }

        mf.EncryptFile(targetUserId);
        return (mf.GetFileData(), oldVer, newVer, buildPatched);
    }
}
