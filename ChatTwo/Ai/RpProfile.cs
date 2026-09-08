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
        // Escalating the wording is not the same as inventing events, and the
        // difference has to be spelled out: the player often writes something
        // mild because their English vocabulary is limited, not because they
        // meant it mildly.
        RpTone.Explicit => "Use warm, sensory, evocative wording; favour texture, heat and movement over plain description. "
                           + "Prefer vivid, specific verbs over generic ones. "
                           + "Be as physically direct as the message allows: take the boldest reasonable reading of what "
                           + "the player wrote rather than the politest one, name things with the frank words adults "
                           + "actually use in this kind of scene instead of clinical or coy ones, and never fade to black. "
                           + "Thai carries intensity in particles that plain English drops, so you may put that intensity "
                           + "back into the wording with swearing, emphasis and the coarser register adults use in the "
                           + "heat of a scene. "
                           + "Saying the same thing more explicitly or more intensely is not inventing and is wanted here; "
                           + "adding actions, participants or events the message does not contain still is not allowed.",
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

    /// <summary>
    /// What the level allows to be added. Always applied, including on
    /// rewrites: this carries the no-invention rule, and dropping it let a
    /// Longer press turn a spoken line into an invented scene.
    /// </summary>
    public static string Instruction(this RpDetail detail) => detail switch
    {
        // These forbid new events, not new words. Forbidding added "dialogue"
        // also forbade saying the same thing at greater length, which left the
        // rewrite buttons with nothing they were allowed to do to a spoken line.
        RpDetail.Faithful =>
            "Convey only what the message actually says. You may choose different words for it, but do not add "
            + "actions, events, participants, sensations or reactions that are not in it.",
        RpDetail.Balanced =>
            "Write flowing prose rather than a literal word-for-word translation. You may reword the message and add "
            + "emphasis freely, but never add actions, events, participants or sensations the original does not contain.",
        RpDetail.Embellish =>
            "Write flowing prose rather than a literal word-for-word translation, and flesh the message out with sensory "
            + "description of what the message itself describes. You may reword and expand freely, but never introduce "
            + "actions, body parts, positions, participants or events the message does not mention.",
        _ => string.Empty,
    };

    /// <summary>
    /// How long the result should be. Left out when a rewrite button drives
    /// the request, because the button decides the length instead.
    /// </summary>
    public static string LengthRule(this RpDetail detail) => detail switch
    {
        RpDetail.Faithful => "Keep the result about as long as the original.",
        RpDetail.Balanced => "Keep the result close to the length of the original message.",
        RpDetail.Embellish => "Follow the length and level of detail of the previous messages, "
                              + "and keep it to a sentence or two when there is no context to follow.",
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
    /// <param name="forRewrite">
    /// True when a rewrite button drives the request. The detail level is left
    /// out then, because the button already says what to change and the level's
    /// "keep close to the original length" pinned spoken lines in place: the
    /// text was already English, so the model kept handing it straight back.
    /// </param>
    public static string? BuildInstruction(Tab? tab, bool forRewrite = false)
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
        if (!forRewrite)
            builder.Append(' ').Append(tab.RpDetail.LengthRule());

        builder.Append(' ').Append(tab.RpTone.Instruction());

        // Last, so no detail or tone setting can talk the model into narrating
        // a line the player meant as speech. Third person belongs to narration
        // only: applying it everywhere turned "it feels good" into "she feels
        // good", and dropping the subject turned an emote into a bare verb.
        builder.Append(" Decide the following from the asterisks alone, never from what the message is about, and let "
                       + "nothing above override it. Text wrapped in *asterisks* is narration: write it in third person "
                       + "present tense with an explicit subject, like *She smiles.* rather than *smiles*. Text that is "
                       + "not wrapped in asterisks is the character speaking out loud: render it as first-person "
                       + "spoken English, the way she would say it aloud, keeping I as I and you as you, and never "
                       + "as narration about the characters. Spoken lines use everyday contractions such as I'm, "
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
