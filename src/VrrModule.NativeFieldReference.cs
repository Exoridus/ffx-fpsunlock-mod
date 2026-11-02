namespace Fahrenheit.Mods.Vrr;

public unsafe sealed partial class VrrModule
{
    private bool init_native_field_reference()
    {
        byte[] body = new byte[NativeFieldTiming.LimiterBodyLength];
        Span<byte> periodBytes = stackalloc byte[8];
        if (!MemoryProbe.TryCopy((nint)FhUtil.ptr_at<byte>(NativeFieldTiming.LimiterBody), body)
            || !NativeFieldTiming.TryFindLimiterOperand(body, out uint periodAddress)
            || !MemoryProbe.TryCopy(unchecked((nint)periodAddress), periodBytes)
            || !NativeFieldTiming.TryGetStepSeconds(BitConverter.ToDouble(periodBytes), out double stepSeconds))
        {
            _logger.Error("[Vrr] Native field reference refused: the native limiter binding or period differs from the supported executable.");
            return false;
        }

        string directory = Environment.GetEnvironmentVariable("FFX_DIAGNOSTICS_DIRECTORY")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fahrenheit", "VRR", "diagnostics");
        string path = Path.Combine(directory, $"native-field-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
        DiagnosticSession session;
        try
        {
            Directory.CreateDirectory(directory);
            session = new DiagnosticSession(path, 8 * 1024 * 1024, 8192,
                new { mode = "native_field_reference", executable_sha256 = NativeBuild.ImageSha256,
                    mod_sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(VrrModule).Assembly.Location))),
                    framework_sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(FhModule).Assembly.Location))),
                    step_seconds = stepSeconds, limiter_period_address = periodAddress,
                    retiming = false, input_replay = false, additional_images = false,
                    coverage = "native host update events and scalar observations; model steps are not measured simulation steps; no geometry or Present proof" });
        }
        catch (Exception error)
        {
            _logger.Error($"[Vrr] Native field reference capture could not start: {error.Message}");
            return false;
        }
        var passes = new NativeFieldReferencePasses(stepSeconds);
        FhApi.Events.Common.GameLoop.PreUpdate.subscribe(args =>
        {
            var pass = passes.Enter(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
            session.TryWrite("native_pre_update", pass.Serial, new { depth = pass.Depth, delta_bits = BitConverter.SingleToInt32Bits(args.delta),
                model_steps = pass.Model?.Steps, model_alpha = pass.Model?.Alpha, model_reset = pass.Model?.Reset,
                divider = VSyncInterval, sg_count = FhUtil.get_at<uint>(EngineAddresses.SgCount),
                vcount = FhUtil.get_at<uint>(EngineAddresses.SgVCount) });
        });
        FhApi.Events.Common.GameLoop.PostUpdate.subscribe(args =>
        {
            long? serial = passes.Exit();
            session.TryWrite("native_post_update", serial ?? 0,
                new { matched = serial.HasValue, delta_bits = BitConverter.SingleToInt32Bits(args.delta), divider = VSyncInterval,
                    sg_count = FhUtil.get_at<uint>(EngineAddresses.SgCount), vcount = FhUtil.get_at<uint>(EngineAddresses.SgVCount) });
        });
        AppDomain.CurrentDomain.ProcessExit += (_, _) => session.Dispose();
        _logger.Info($"[Vrr] Native field reference: step={stepSeconds:R}s; native timing and input retained, no mod hooks or patches installed. Capture: {path}");
        return true;
    }
}
