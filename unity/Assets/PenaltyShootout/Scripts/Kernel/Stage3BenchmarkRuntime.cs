using System;

namespace PenaltyShootout.Kernel
{
    public static class Stage3BenchmarkRuntime
    {
        public const string MasterSeedArgument = "--stage3-master-seed";

        public static void ApplyOverrides(
            PenaltyAreaController controller,
            string[] args = null)
        {
            if (controller == null)
            {
                return;
            }

            if (TryParseMasterSeed(
                    args ?? Environment.GetCommandLineArgs(),
                    out var masterSeed))
            {
                controller.MasterSeed = masterSeed;
            }
        }

        public static bool TryParseMasterSeed(string[] args, out ulong masterSeed)
        {
            masterSeed = 0UL;
            if (args == null)
            {
                return false;
            }

            for (var index = 0; index < args.Length; index++)
            {
                var argument = args[index];
                if (string.IsNullOrEmpty(argument))
                {
                    continue;
                }

                if (argument.StartsWith(MasterSeedArgument + "=", StringComparison.Ordinal))
                {
                    return ulong.TryParse(
                        argument.Substring(MasterSeedArgument.Length + 1),
                        out masterSeed);
                }

                if (argument == MasterSeedArgument && index + 1 < args.Length)
                {
                    return ulong.TryParse(args[index + 1], out masterSeed);
                }
            }

            return false;
        }
    }
}
