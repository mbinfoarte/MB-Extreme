namespace MBExtreme.Core.Licensing;

/// <summary>Fluxo de login do aplicativo: pede um código, o usuário aprova no site e o app recebe a licença.</summary>
public static class LicenseFlow
{
    public static async Task<PollResult> LoginAsync(
        LicenseClient client, string deviceId, string deviceName, Action<DeviceCode> onCode,
        CancellationToken token, Func<TimeSpan, CancellationToken, Task>? delay = null, Func<DateTimeOffset>? clock = null, DeviceInfo? info = null)
    {
        delay ??= (span, ct) => Task.Delay(span, ct);
        clock ??= () => DateTimeOffset.UtcNow;

        var code = await client.StartDeviceLoginAsync(deviceId, deviceName, token, info);
        onCode(code);

        var deadline = clock() + TimeSpan.FromSeconds(Math.Max(30, code.ExpiresInSeconds));
        var interval = TimeSpan.FromSeconds(Math.Max(2, code.IntervalSeconds));
        int failures = 0;

        while (clock() < deadline)
        {
            await delay(interval, token);
            try
            {
                var result = await client.PollAsync(code.DeviceCodeValue, deviceId, token);
                failures = 0;
                switch (result.State)
                {
                    case PollState.Approved:
                    case PollState.Denied:
                    case PollState.Expired:
                        return result;
                    case PollState.SlowDown:
                        interval += TimeSpan.FromSeconds(5);
                        break;
                }
            }
            catch (LicenseNetworkException) when (++failures < 4)
            {
                // queda curta de internet: tenta de novo no próximo intervalo
            }
        }

        return new PollResult(PollState.Expired, null, "O código expirou. Clique em Entrar para pedir outro.");
    }
}
