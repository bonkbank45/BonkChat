using System.Text;

namespace ChatTwo.Ai;

[Serializable]
public enum RpPronoun
{
    They,
    He,
    She,
}

[Serializable]
public enum RpTone
{
    Neutral,
    Sensual,
    Explicit,
}

public static class RpExt
{
    public static string Subject(this RpPronoun pronoun) => pronoun switch
    {
        RpPronoun.He => "he/him/his",
        RpPronoun.She => "she/her/hers",
        _ => "they/them/their",
    };

    public static string Name(this RpTone tone) => tone.ToString();

    // Each level builds on the one before it: picking Explicit used to drop
    // the evocative wording that Sensual asks for, which made it read flatter
    // than the level below it.
    public static string Instruction(this RpTone tone) => tone switch
    {
        RpTone.Neutral => "Keep the wording plain and descriptive.",
        RpTone.Sensual => "Use warm, sensory, evocative wording; favour texture, heat and movement over plain description. "
                          + "Prefer vivid, specific verbs over generic ones.",
        RpTone.Explicit => "Use warm, sensory, evocative wording; favour texture, heat and movement over plain description. "
                           + "Prefer vivid, specific verbs over generic ones. "
                           + "Be direct and explicit where the scene calls for it: use blunt, unambiguous adult vocabulary "
                           + "rather than euphemism, and never fade to black.",
        _ => string.Empty,
    };

    public static string Description(this RpTone tone) => tone switch
    {
        RpTone.Neutral => "Plain description, no added colour.",
        RpTone.Sensual => "Evocative and sensory wording.",
        RpTone.Explicit => "Direct wording, no euphemisms.",
        _ => string.Empty,
    };
}

/// <summary>
/// Turns a tab's roleplay settings into the instruction block appended to the
/// prompt. Built on the main thread because it reads the game's player name;
/// the request itself then runs in the background with a plain string.
/// </summary>
public static class RpProfile
{
    public static string? BuildInstruction(Tab? tab)
    {
        if (tab is not { RoleplayMode: true })
            return null;

        var self = tab.RpSelfName.Trim();
        if (self.Length == 0)
            self = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty;

        var partner = tab.RpPartnerName.Trim();
        if (partner.Length == 0 && tab.TellTarget.IsSet())
            partner = tab.TellTarget.Name;

        var builder = new StringBuilder();
        // The base prompt already establishes that this is roleplay prose.
        builder.Append(" Write in third person, present tense.");

        if (self.Length > 0)
            builder.Append($" My character is {self}, referred to as {tab.RpSelfPronoun.Subject()}.");
        else
            builder.Append($" My character is referred to as {tab.RpSelfPronoun.Subject()}.");

        if (partner.Length > 0)
            builder.Append($" The other character is {partner}, referred to as {tab.RpPartnerPronoun.Subject()}.");
        else
            builder.Append($" The other character is referred to as {tab.RpPartnerPronoun.Subject()}.");

        builder.Append(" Asterisks and quotation marks are roleplay formatting, not markdown, so they must survive: "
                       + "if the message is wrapped in asterisks then your reply must be wrapped in asterisks too, "
                       + "and any speech in quotation marks stays in quotation marks.");
        builder.Append(" The names are for your reference only: refer to the characters by pronoun, "
                       + "and only write a name when the original message names someone.");
        builder.Append(" Write flowing prose rather than a literal word-for-word translation.");
        builder.Append(" Unless an instruction below says otherwise, match the length and level of detail of the previous messages.");
        builder.Append(" Never invent actions, thoughts or dialogue that are not in the original.");
        builder.Append(' ').Append(tab.RpTone.Instruction());

        var extra = tab.RpExtraInstruction.Trim();
        if (extra.Length > 0)
            builder.Append(' ').Append(extra);

        return builder.ToString();
    }
}
