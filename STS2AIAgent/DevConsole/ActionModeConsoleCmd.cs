using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using STS2AIAgent.Game;

namespace STS2AIAgent.DebugConsole;

internal sealed class ActionModeConsoleCmd : AbstractConsoleCmd
{
    private static readonly string[] SupportedModes = ["stable", "instant"];

    public override string CmdName => "sts2_action_mode";

    public override string Args => "[stable|instant]";

    public override string Description => "Get or set STS2AIAgent default action mode.";

    public override bool IsNetworked => true;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length == 0)
        {
            return new CmdResult(success: true, $"Current default action mode: {GameActionService.GetDefaultExecutionMode()}");
        }

        if (args.Length != 1)
        {
            return new CmdResult(success: false, $"Usage: {CmdName} {Args}");
        }

        try
        {
            var mode = GameActionService.SetDefaultExecutionMode(args[0]);
            return new CmdResult(success: true, $"Default action mode set to {mode}.");
        }
        catch (Exception ex)
        {
            return new CmdResult(success: false, ex.Message);
        }
    }

    public override CompletionResult GetArgumentCompletions(Player? player, string[] args)
    {
        if (args.Length > 1)
        {
            return base.GetArgumentCompletions(player, args);
        }

        var partialArg = args.Length == 0 ? string.Empty : args[^1];
        var completedArgs = args.Length == 0 ? [] : args[..^1];
        return CompleteArgument(SupportedModes, completedArgs, partialArg, CompletionType.Argument, StringComparer.OrdinalIgnoreCase.Equals);
    }
}
