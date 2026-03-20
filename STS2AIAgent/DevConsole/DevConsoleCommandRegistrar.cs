using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Debug;

namespace STS2AIAgent.DebugConsole;

internal static class DevConsoleCommandRegistrar
{
    private const string LogPrefix = "[STS2AIAgent.DevConsole]";

    private static readonly object Gate = new();
    private static readonly MethodInfo? RegisterCommandMethod = typeof(MegaCrit.Sts2.Core.DevConsole.DevConsole).GetMethod(
        "RegisterCommand",
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        types: [typeof(AbstractConsoleCmd)],
        modifiers: null);

    private static WeakReference<MegaCrit.Sts2.Core.DevConsole.DevConsole>? _registeredConsole;

    internal static void TryRegisterExistingConsole()
    {
        try
        {
            TryRegister(NDevConsole.Instance);
        }
        catch (Exception ex)
        {
            Log.Debug($"{LogPrefix} Native console is not available yet: {ex.Message}");
        }
    }

    internal static void TryRegister(NDevConsole? consoleNode)
    {
        if (consoleNode == null)
        {
            return;
        }

        var console = GetDevConsoleCore(consoleNode);
        if (console == null || RegisterCommandMethod == null)
        {
            return;
        }

        lock (Gate)
        {
            if (_registeredConsole != null &&
                _registeredConsole.TryGetTarget(out var registeredConsole) &&
                ReferenceEquals(registeredConsole, console))
            {
                return;
            }

            RegisterCommandMethod.Invoke(console, [new ActionModeConsoleCmd()]);
            _registeredConsole = new WeakReference<DevConsole>(console);
            Log.Info($"{LogPrefix} Registered native command 'sts2_action_mode'.");
        }
    }

    private static MegaCrit.Sts2.Core.DevConsole.DevConsole? GetDevConsoleCore(NDevConsole console)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var field = typeof(NDevConsole).GetField("_devConsole", flags);
        return field?.GetValue(console) as MegaCrit.Sts2.Core.DevConsole.DevConsole;
    }

    [HarmonyPatch(typeof(NDevConsole), nameof(NDevConsole._Ready))]
    private static class NDevConsoleReadyPatch
    {
        private static void Postfix(NDevConsole __instance)
        {
            TryRegister(__instance);
        }
    }

    [HarmonyPatch(typeof(NDevConsole), nameof(NDevConsole._ExitTree))]
    private static class NDevConsoleExitTreePatch
    {
        private static void Postfix(NDevConsole __instance)
        {
            var console = GetDevConsoleCore(__instance);
            if (console == null)
            {
                return;
            }

            lock (Gate)
            {
                if (_registeredConsole != null &&
                    _registeredConsole.TryGetTarget(out var registeredConsole) &&
                    ReferenceEquals(registeredConsole, console))
                {
                    _registeredConsole = null;
                }
            }
        }
    }
}
