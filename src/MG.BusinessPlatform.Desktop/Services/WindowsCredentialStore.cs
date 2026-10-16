using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace MG.BusinessPlatform.Desktop.Services;

/// <summary>
/// Stores small secrets in the current Windows user's Credential Manager rather than
/// application settings files or logs.
/// </summary>
internal static class WindowsCredentialStore
{
    private const uint GenericCredentialType = 1;
    private const uint PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("Advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    public static void Save(string targetName, string secret)
    {
        EnsureWindows();
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        if (secret.Contains('\0'))
            throw new ArgumentException("Credential values cannot contain a null character.", nameof(secret));

        var bytes = Encoding.Unicode.GetBytes(secret);
        if (bytes.Length > 2560)
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new ArgumentOutOfRangeException(nameof(secret), "Windows Credential Manager limits a credential blob to 2560 bytes.");
        }

        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Flags = 0,
                Type = GenericCredentialType,
                TargetName = targetName,
                Comment = "MG Business Platform encrypted-by-Windows credential store",
                LastWritten = 0,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                TargetAlias = null,
                UserName = Environment.UserName
            };

            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not save the credential in Windows Credential Manager.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            Marshal.ZeroFreeHGlobal(blob);
        }
    }

    public static string? Read(string targetName)
    {
        EnsureWindows();
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        if (!CredRead(targetName, GenericCredentialType, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
                return null;
            throw new Win32Exception(error, "Could not read the credential from Windows Credential Manager.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
                return null;
            if (credential.CredentialBlobSize > 2560)
                throw new InvalidDataException("The saved Windows credential exceeds the supported size limit.");

            var bytes = new byte[checked((int)credential.CredentialBlobSize)];
            try
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public static void Delete(string targetName)
    {
        EnsureWindows();
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        if (CredDelete(targetName, GenericCredentialType, 0))
            return;

        var error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound)
            throw new Win32Exception(error, "Could not delete the credential from Windows Credential Manager.");
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Credential Manager is available only on Windows.");
    }
}
