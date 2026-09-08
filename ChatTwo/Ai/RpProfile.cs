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

/// <summary>
/// How much the AI may add on top of what was actually written. Separate from
/// tone: tone picks the words, this decides how much text there is at all.
/// </summary>
[Serializable]
public enum RpDetail
{
    Faithful,
    Balanced,
    Embellish,
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

    public static string Name(this RpDetail detail) => detail.ToString();

    public static string Instruction(this RpDetail detail) => detail switch
    {
        RpDetail.Faithful =>
            "Translate only what the message actually says. Do not add actions, sensations, reactions or narration "
            + "that are not in it, and keep the result about as long as the original.",
        RpDetail.Balanced =>
            "Write flowing prose rather than a literal word-for-word translation, but never add actions, thoughts, "
            + "dialogue or sensations that are not in the original. Keep the result close to the length of the original message.",
        RpDetail.Embellish =>
            "Write flowing prose rather than a literal word-for-word translation, and flesh the message out with sensory "
            + "description of what the message itself describes, following the length and level of detail of the previous "
            + "messages. Never introduce actions, body parts, positions, participants or dialogue the message does not "
            + "mention, and keep it to a sentence or two when there is no context to follow.",
        _ => string.Empty,
    };

    public static string Description(this RpDetail detail) => detail switch
    {
        RpDetail.Faithful => "Only what you wrote, nothing added.",
        RpDetail.Balanced => "Smoother wording, same events and length.",
        RpDetail.Embellish => "Fills your line out into fuller prose.",
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

        // Scoped to narration on purpose. Declaring the pronouns without that
        // scope kept pulling spoken lines into the third person, so "it feels
        // good" came back as "she feels good".
        var selfIs = self.Length > 0 ? $" My character is {self}." : " ";
        builder.Append($"{selfIs} Use {tab.RpSelfPronoun.Subject()} for my character in narration only; "
                       + "in spoken lines my character speaks in the first person as I.");

        var partnerIs = partner.Length > 0 ? $" The other character is {partner}." : " ";
        builder.Append($"{partnerIs} Use {tab.RpPartnerPronoun.Subject()} for the other character in narration only; "
                       + "in spoken lines my character addresses them as you.");

        builder.Append(" Reproduce the message's formatting exactly. Asterisks and quotation marks are roleplay "
                       + "formatting rather than markdown: keep the ones that are there, and never add asterisks "
                       + "or quotation marks that the message does not already have.");
        builder.Append(" The names are for your reference only: refer to the characters by pronoun, "
                       + "and only write a name when the original message names someone.");
        builder.Append(" Translate the whole message into English. Never leave Thai in your reply, "
                       + "and never repeat the player's original text alongside the translation.");
        builder.Append(' ').Append(tab.RpDetail.Instruction());
        builder.Append(' ').Append(tab.RpTone.Instruction());

        // Last, so no detail or tone setting can talk the model into narrating
        // a line the player meant as speech. Third person belongs to narration
        // only: applying it everywhere turned "it feels good" into "she feels
        // good", and dropping the subject turned an emote into a bare verb.
        builder.Append(" Decide the following from the asterisks alone, never from what the message is about, and let "
                       + "nothing above override it. Text wrapped in *asterisks* is narration: write it in third person "
                       + "present tense with an explicit subject, like *She smiles.* rather than *smiles*. Text that is "
                       + "not wrapped in asterisks is the character speaking out loud: translate it as first-person "
                       + "spoken English exactly as she would say it, keeping I as I and you as you, and never rewrite "
                       + "it as narration about the characters. Spoken lines use everyday contractions such as I'm, "
                       + "don't, can't and you're, the way people actually talk, never stiff textbook phrasing. "
                       + "Quotation marks around speech are part of the message: if the player wrote them, your reply "
                       + "keeps them in the same place.");

        // Marked as an override, or the no-invention rules above win and the
        // player's own instruction is quietly ignored.
        var extra = tab.RpExtraInstruction.Trim();
        if (extra.Length > 0)
            builder.Append(" The player's own instruction, which takes priority over everything above: ").Append(extra);

        return builder.ToString();
    }
}
