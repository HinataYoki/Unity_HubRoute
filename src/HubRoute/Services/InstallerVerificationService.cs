using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HubRoute.Models;

namespace HubRoute.Services;

/// <summary>Validates downloaded installers with the current platform's native trust service.</summary>
public sealed class InstallerVerificationService
{
    private const string ExpectedPublisherPrefix = "Unity Technologies";
    private const uint NoUserInterface = 2;
    private const uint FileChoice = 1;
    private static readonly Guid GenericVerifyAction =
        new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    /// <summary>Checks the native signature and confirms that Unity is the declared publisher.</summary>
    public async Task<InstallerVerificationResult> VerifyAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return new InstallerVerificationResult(false, "安装包不存在。");
        }

        if (OperatingSystem.IsWindows())
        {
            return VerifyWindows(filePath);
        }

        if (OperatingSystem.IsMacOS())
        {
            return await VerifyMacOsAsync(filePath, cancellationToken);
        }

        return new InstallerVerificationResult(false, "当前平台不支持安装包签名校验。");
    }

    /// <summary>Runs WinVerifyTrust and checks the embedded signer's display name.</summary>
    [SupportedOSPlatform("windows")]
    private static InstallerVerificationResult VerifyWindows(string filePath)
    {
        if (!VerifyWindowsTrust(filePath))
        {
            return new InstallerVerificationResult(false, "Windows 不信任该安装包的数字签名。");
        }

        try
        {
            // No current X509CertificateLoader API extracts an Authenticode signer from a PE file.
#pragma warning disable SYSLIB0057
            using var signedFileCertificate = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
            using var certificate = X509CertificateLoader.LoadCertificate(
                signedFileCertificate.GetRawCertData());
            var publisher = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            return IsExpectedPublisher(publisher)
                ? new InstallerVerificationResult(true, $"签名有效：{publisher}")
                : new InstallerVerificationResult(false, $"安装包发布者不是 Unity：{publisher}");
        }
        catch (CryptographicException)
        {
            return new InstallerVerificationResult(false, "无法读取安装包的签名证书。");
        }
    }

    /// <summary>Asks macOS Gatekeeper to assess the disk image and validates its origin text.</summary>
    [SupportedOSPlatform("macos")]
    private static async Task<InstallerVerificationResult> VerifyMacOsAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("/usr/sbin/spctl")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("--assess");
            process.StartInfo.ArgumentList.Add("--type");
            process.StartInfo.ArgumentList.Add("open");
            process.StartInfo.ArgumentList.Add("--context");
            process.StartInfo.ArgumentList.Add("context:primary-signature");
            process.StartInfo.ArgumentList.Add("--verbose=2");
            process.StartInfo.ArgumentList.Add(filePath);

            if (!process.Start())
            {
                return new InstallerVerificationResult(false, "无法启动 macOS Gatekeeper 校验。");
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var details = $"{await standardOutput} {await standardError}".Trim();
            if (process.ExitCode != 0)
            {
                return new InstallerVerificationResult(false, "macOS Gatekeeper 不信任该安装包。");
            }

            return IsExpectedPublisher(details)
                ? new InstallerVerificationResult(true, "Gatekeeper 已确认 Unity Technologies 发布者。")
                : new InstallerVerificationResult(false, "Gatekeeper 结果不包含 Unity 发布者信息。");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or IOException)
        {
            return new InstallerVerificationResult(false, "无法完成 macOS Gatekeeper 校验。");
        }
    }

    /// <summary>Recognizes Unity publisher names without binding to a renewable certificate.</summary>
    internal static bool IsExpectedPublisher(string? publisher)
    {
        return publisher?.Contains(ExpectedPublisherPrefix, StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>Invokes the Windows Authenticode policy provider without displaying system UI.</summary>
    [SupportedOSPlatform("windows")]
    private static bool VerifyWindowsTrust(string filePath)
    {
        var fileInfo = new WinTrustFileInfo(filePath);
        var fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        var trustDataPointer = IntPtr.Zero;
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, fDeleteOld: false);
            var trustData = new WinTrustData(fileInfoPointer);
            trustDataPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(trustData, trustDataPointer, fDeleteOld: false);

            var action = GenericVerifyAction;
            return WinVerifyTrust(new IntPtr(-1), ref action, trustDataPointer) == 0;
        }
        finally
        {
            if (trustDataPointer != IntPtr.Zero)
            {
                Marshal.DestroyStructure<WinTrustData>(trustDataPointer);
                Marshal.FreeHGlobal(trustDataPointer);
            }

            Marshal.DestroyStructure<WinTrustFileInfo>(fileInfoPointer);
            Marshal.FreeHGlobal(fileInfoPointer);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WinVerifyTrust(
        IntPtr windowHandle,
        ref Guid actionId,
        IntPtr trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        internal WinTrustFileInfo(string filePath)
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
            FilePath = filePath;
            FileHandle = IntPtr.Zero;
            KnownSubject = IntPtr.Zero;
        }

        private uint StructSize;

        [MarshalAs(UnmanagedType.LPWStr)]
        private string FilePath;

        private IntPtr FileHandle;
        private IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        internal WinTrustData(IntPtr fileInfo)
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustData>();
            PolicyCallbackData = IntPtr.Zero;
            SipClientData = IntPtr.Zero;
            UserInterfaceChoice = NoUserInterface;
            RevocationChecks = 0;
            UnionChoice = FileChoice;
            FileInfo = fileInfo;
            StateAction = 0;
            StateData = IntPtr.Zero;
            UrlReference = IntPtr.Zero;
            ProviderFlags = 0;
            UserInterfaceContext = 0;
        }

        private uint StructSize;
        private IntPtr PolicyCallbackData;
        private IntPtr SipClientData;
        private uint UserInterfaceChoice;
        private uint RevocationChecks;
        private uint UnionChoice;
        private IntPtr FileInfo;
        private uint StateAction;
        private IntPtr StateData;
        private IntPtr UrlReference;
        private uint ProviderFlags;
        private uint UserInterfaceContext;
    }
}
