using FluentFTP;
using IconicLauncher.Core.Models;
using IconicLauncher.Core.Utils;
using Serilog;

namespace IconicLauncher.Core.Services;

public sealed class FtpPublishService : IFtpPublishService
{
    public async Task UploadAsync(AdminSettings admin, string localPath, string remoteFileName, IProgress<double> progress, CancellationToken ct = default)
    {
        var password = DpapiProtector.Unprotect(admin.FtpPasswordProtected);
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("FTP password could not be decrypted");
        }

        var remotePath = admin.RemotePath.TrimEnd('/') + "/" + remoteFileName;
        var client = new AsyncFtpClient(admin.FtpHost, admin.FtpUser, password, admin.FtpPort);
        try
        {
            Log.Information("FTP connecting to {Host}:{Port} as {User}", admin.FtpHost, admin.FtpPort, admin.FtpUser);
            await client.Connect(ct);

            var ftpProgress = new Progress<FtpProgress>(p =>
            {
                if (p.Progress >= 0)
                {
                    progress.Report(Math.Clamp(p.Progress / 100.0, 0.0, 1.0));
                }
            });

            var status = await client.UploadFile(localPath, remotePath, FtpRemoteExists.Overwrite, true, FtpVerify.None, ftpProgress, ct);
            if (status == FtpStatus.Failed)
            {
                throw new InvalidOperationException($"FTP upload failed for {remotePath}");
            }

            progress.Report(1.0);
            Log.Information("FTP uploaded {Local} to {Remote}", localPath, remotePath);
        }
        finally
        {
            try
            {
                if (client.IsConnected)
                {
                    await client.Disconnect(CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "FTP disconnect failed for {Host}", admin.FtpHost);
            }
            client.Dispose();
        }
    }
}
