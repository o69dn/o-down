using System.IO.Pipes;
using o_down.Core.Protocol;

namespace o_down.NativeMessaging;

internal static class Program
{
    private const string DefaultPipeName = "o-down-link";
    private const string AppVersion = "0.1.0";
    private const string PipeNameEnv = "ODOWN_PIPE_NAME";
    private const string HostLogEnv = "ODOWN_HOST_LOG";

    private static async Task<int> Main(string[] args)
    {
        var logPath = Environment.GetEnvironmentVariable(HostLogEnv);
        async Task Log(string msg)
        {
            if (logPath is not null)
            {
                try { await File.AppendAllTextAsync(logPath, $"[{DateTimeOffset.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}").ConfigureAwait(false); } catch { }
            }
        }

        try
        {
            var pipeName = Environment.GetEnvironmentVariable(PipeNameEnv);
            if (string.IsNullOrEmpty(pipeName)) pipeName = DefaultPipeName;
            await Log($"host starting; pipe={pipeName}");

            using var stdin = Console.OpenStandardInput();
            using var stdout = Console.OpenStandardOutput();

            while (true)
            {
                NativeMessageCodec.NativeResponse response;
                try
                {
                    var link = await NativeMessageCodec.ReadRequestAsync(stdin).ConfigureAwait(false);
                    if (link is null)
                    {
                        await Log("stdin EOF; exiting");
                        return 0;
                    }
                    await Log($"got url={link.Url} source={link.Source}");
                    response = await ForwardToAppAsync(link, pipeName).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await Log($"handler error: {ex.GetType().Name}: {ex.Message}");
                    response = new NativeMessageCodec.NativeResponse { Ok = false, Error = ex.Message, Version = AppVersion };
                }

                response.Version ??= AppVersion;
                var bytes = NativeMessageCodec.EncodeResponse(response);
                await stdout.WriteAsync(bytes).ConfigureAwait(false);
                await stdout.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await Log($"fatal: {ex}");
            return 0;
        }
    }

    private static async Task<NativeMessageCodec.NativeResponse> ForwardToAppAsync(Core.Abstractions.CapturedLink link, string pipeName)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await client.ConnectAsync(2000).ConfigureAwait(false);

            var requestBytes = NativeMessageCodec.EncodeRequest(link);
            await client.WriteAsync(requestBytes).ConfigureAwait(false);
            await client.FlushAsync().ConfigureAwait(false);

            var response = await NativeMessageCodec.ReadResponseAsync(client).ConfigureAwait(false);
            if (response is null)
                return new NativeMessageCodec.NativeResponse { Ok = false, Error = "empty response from o-down" };
            return response;
        }
        catch (TimeoutException)
        {
            return new NativeMessageCodec.NativeResponse { Ok = false, Error = "o-down is not running" };
        }
        catch (Exception ex)
        {
            return new NativeMessageCodec.NativeResponse { Ok = false, Error = ex.Message };
        }
    }
}
