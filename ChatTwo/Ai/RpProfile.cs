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

    /// <summary> The subject form on its own, for checking a narration has one. </summary>
    public static string SubjectWord(this RpPronoun pronoun) => pronoun switch
    {
        RpPronoun.He => "he",
        RpPronoun.She => "she",
        _ => "they",
    };

    /// <summary>
    /// The set's words, for checking a reply used the right one. "them" and
    /// "themselves" are left out: they refer to things as readily as to
    /// people, and *She pins his wrists and holds them down* was being
    /// rejected for a pronoun that was talking about the wrists.
    /// </summary>
    public static string[] Words(this RpPronoun pronoun) => pronoun switch
    {
        RpPronoun.He => ["he", "him", "his", "himself"],
        RpPronoun.She => ["she", "her", "hers", "herself"],
        _ => ["they", "their", "theirs"],
    };

    public static string Name(this RpTone tone) => tone.ToString();

    // Each level builds on the one before it, and each one has to claim
    // register the level below it is denied: when the levels only differed by
    // encouragement, Neutral and Sensual came back word for word identical and
    // the slider had two working positions out of three.
    public static string Instruction(this RpTone tone) => tone switch
    {
        RpTone.Neutral => "Keep the wording plain and factual, with no added sensory colour, no profanity "
                          + "and no suggestive phrasing beyond what the message itself states.",
        // Given its own territory - charged but never crude - so that it is
        // measurably different from both of its neighbours.
        RpTone.Sensual => "Use warm, sensory, evocative wording: favour texture, heat, breath and movement over plain "
                          + "description, and prefer vivid, specific verbs over generic ones. Let the wording carry that "
                          + "this is an intimate scene even when the message itself is plainly worded, but stay "
                          + "suggestive rather than crude: no profanity and no blunt anatomical words at this level. "
                          // Without this the level had nothing to do with a
                          // six-word spoken line and came back word for word
                          // identical to the level below it.
                          + "This applies to a short spoken line too: reach for the warmer, more charged word rather "
                          + "than the flat one, so the line still reads as something said in the middle of this scene. "
                          // Otherwise this fights the detail levels, which
                          // forbid added sensations: told to be sensory, the
                          // model supplied a taste the message never had.
                          + "All of that richness describes only what the message already names. Choose a warmer word "
                          + "for a thing that is there; never supply a sensation, taste, smell, sound or body part the "
                          + "message does not mention.",
        // Escalating the wording is not the same as inventing events, and the
        // difference has to be spelled out: the player often writes something
        // mild because their English vocabulary is limited, not because they
        // meant it mildly.
        RpTone.Explicit => "Use warm, sensory, evocative wording: favour texture, heat, breath and movement over plain "
                           + "description, and prefer vivid, specific verbs over generic ones. "
                           + "On top of that, be as physically direct as the message allows: take the boldest reasonable "
                           + "reading of what the player wrote rather than the politest one, name things with the frank "
                           + "words adults actually use in this kind of scene instead of clinical or coy ones, and never "
                           + "fade to black. "
                           + "Thai carries intensity in particles that plain English drops, so put that intensity back "
                           + "into the wording with swearing, emphasis and the coarser register adults use in the heat "
                           + "of a scene. "
                           // A rule naming Thai intensifier particles and asking
                           // for a swear word in their place stood here. Over
                           // two runs it produced "fuck", "cum" and a pair of
                           // invented demands on messages that had none, while
                           // the mild lines it was written for came back just
                           // as mild: it bought invention and no intensity.

                           + "This matters most when the message reads mildly. A mild line is the limit of the player's "
                           + "English, not a request for a mild translation, so it still comes out in this register: "
                           + "choose the blunt word over the polite one, and let the line swear the way the player would "
                           + "if the words came easily to them. A reply that could be read aloud in polite company has "
                           + "not done its job at this level. "
                           // Given a free hand, this level stopped translating
                           // and started topping the line up: "fuck me hard"
                           // came back as "fuck me hard, really pound me deep
                           // and rough", and "I like it there" as "fuck me
                           // there".
                           + "The bluntness goes into the words, not into the events. Swearing and coarse register "
                           + "belong in the emphasis and in the frank name for something the message already refers "
                           + "to; they never arrive as a new verb. If the message does not say what is being done, "
                           + "your reply does not name an act either: a message about liking something stays about "
                           + "liking it, however bluntly it says so. Never append an extra phrase, a second demand or "
                           + "a restatement in stronger terms. "
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
            "Write flowing prose rather than a literal word-for-word translation, and flesh the message out: spell out "
            + "the same actions and moments the message already contains, drawing each of them out instead of stating "
            + "it once. You may reword and expand freely, but never introduce actions, body parts, positions, "
            + "participants or events the message does not mention. "
            // Told only to add sensory detail, this level filled a three-word
            // emote with anatomy and sensations the player never wrote.
            // "A result close to the original is correct" stood here as an
            // escape from overshooting, and the level took it every time:
            // measured over two samples it came out shorter than Faithful.
            + "The expansion is in the phrasing, not in new material: do not name a body part, a sensation or a "
            + "physical state that the message does not name. Reach for the fuller way of saying the same thing "
            + "rather than the shortest one, so the result still reads as the richest of the three levels.",
        _ => string.Empty,
    };

    /// <summary>
    /// How long the result should be. Left out when a rewrite button drives
    /// the request, because the button decides the length instead.
    /// </summary>
    /// <remarks>
    /// These have to name lengths that differ, not describe the same length in
    /// three ways. "About as long" and "close to the length" turned out to be
    /// the same instruction, and Embellish's old "keep it to a sentence or two"
    /// capped it below the level underneath it: measured over two samples each,
    /// all three levels produced the same number of characters.
    /// </remarks>
    public static string LengthRule(this RpDetail detail) => detail switch
    {
        RpDetail.Faithful => "Keep the result as short as the original: no clause that the original does not have.",
        RpDetail.Balanced => "The result may run somewhat longer than the original where natural English needs the "
                             + "words, but it stays one message of roughly the same shape.",
        RpDetail.Embellish => "Where there is something to draw out, the result may run up to about half again as long "
                              + "as the original, but no longer, and it stays one message. When there is context to "
                              + "follow, match the length and level of detail of the previous messages instead.",
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
/// <summary>
/// The parts of a tab's roleplay settings that the reply can be checked
/// against without understanding it. Declaring the pronouns in the prompt was
/// not enough on its own: faced with two characters sharing a pronoun set, the
/// model would switch one of them to the second person, borrow a set neither
/// character uses, or reach for a name to tell them apart.
/// </summary>
/// <param name="NamesAllowed">
/// True when both characters share a pronoun set, where a name is the only way
/// to say which of them is meant.
/// </param>
/// <param name="Expands">
/// True at the Embellish level, where a result with more clauses in it than
/// the message is the point rather than a sign of padding.
/// </param>
public readonly record struct RpGuard(
    string[] ForbiddenPronouns,
    string[] SubjectWords,
    string SelfName,
    string PartnerName,
    bool NamesAllowed,
    bool Expands);

public static class RpProfile
{
    /// <summary> The names shown to the AI, falling back to the game's own. </summary>
    private static (string Self, string Partner) ResolveNames(Tab tab)
    {
        var self = tab.RpSelfName.Trim();
        if (self.Length == 0)
            self = Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? string.Empty;

        var partner = tab.RpPartnerName.Trim();
        if (partner.Length == 0 && tab.TellTarget.IsSet())
            partner = tab.TellTarget.Name;

        return (self, partner);
    }

    /// <summary>
    /// The checkable half of the tab's settings. Built on the main thread with
    /// the instruction block, for the same reason: it reads the player name.
    /// </summary>
    public static RpGuard? BuildGuard(Tab? tab)
    {
        if (tab is not { RoleplayMode: true })
            return null;

        var used = new[] { tab.RpSelfPronoun, tab.RpPartnerPronoun }.Distinct().ToArray();
        var (self, partner) = ResolveNames(tab);

        return new RpGuard(
            Enum.GetValues<RpPronoun>().Except(used).SelectMany(pronoun => pronoun.Words()).ToArray(),
            used.Select(pronoun => pronoun.SubjectWord()).ToArray(),
            self,
            partner,
            tab.RpSelfPronoun == tab.RpPartnerPronoun,
            tab.RpDetail == RpDetail.Embellish);
    }

    /// <param name="forRewrite">
    /// True when a rewrite button drives the request. The detail level is left
    /// out then, because the button already says what to change and the level's
    /// "keep close to the original length" pinned spoken lines in place: the
    /// text was already English, so the model kept handing it straight back.
    /// </param>
    /// <param name="hasEmoteMarkup">
    /// Whether the message contains asterisks. When it does not, the whole
    /// narration half of the rules is left out instead of being sent and then
    /// argued with: keeping it made the model wrap spoken lines in invented
    /// emotes, or freeze and hand the line straight back.
    /// </param>
    public static string? BuildInstruction(Tab? tab, bool forRewrite = false, bool hasEmoteMarkup = true)
    {
        if (tab is not { RoleplayMode: true })
            return null;

        var (self, partner) = ResolveNames(tab);

        var builder = new StringBuilder();

        var selfIs = self.Length > 0 ? $" My character is {self}." : string.Empty;
        var partnerIs = partner.Length > 0 ? $" The other character is {partner}." : string.Empty;

        if (hasEmoteMarkup)
        {
            // Scoped to narration on purpose. Declaring the pronouns without
            // that scope kept pulling spoken lines into the third person, so
            // "it feels good" came back as "she feels good".
            builder.Append($"{selfIs} Use {tab.RpSelfPronoun.Subject()} for my character in narration only; "
                           + "in spoken lines my character speaks in the first person as I.");
            builder.Append($"{partnerIs} Use {tab.RpPartnerPronoun.Subject()} for the other character in narration "
                           + "only; in spoken lines my character addresses them as you.");
            // Every one of these forbids an escape the model actually took when
            // the pronouns were merely declared: it wrote the partner as "your",
            // swapped in a pronoun neither character uses, reached for a name to
            // tell two same-pronoun characters apart, and dropped the subject
            // altogether to avoid choosing.
            builder.Append(" Those two pronoun sets are fixed and they are the only third-person pronouns your reply "
                           + "may use. Narration is never written in the second person: neither character is you or "
                           + "your there.");

            // Holding the pronouns and banning names at once produced
            // *He straddles his lap and presses his hips down*, which is
            // correct and unreadable. A name is the only way out of that.
            builder.Append(tab.RpSelfPronoun == tab.RpPartnerPronoun
                ? " Both characters use the same pronouns, so keep those pronouns and, where one alone would leave it "
                  + "unclear which character is meant, write that character's name for that mention instead of "
                  + "reaching for a pronoun neither of them uses."
                : " When a pronoun would be ambiguous, make who is who clear from the sentence rather than swapping "
                  + "in a different pronoun or falling back on a name.");

            builder.Append(" Every narrated sentence keeps an explicit subject even when that repeats the pronoun, "
                           + "and singular they takes a plural verb, as in they straddle rather than they straddles.");
            builder.Append(" Reproduce the message's formatting exactly. Asterisks and quotation marks are roleplay "
                           + "formatting rather than markdown: keep the ones that are there, and never add asterisks "
                           + "or quotation marks that the message does not already have.");
        }
        else
        {
            builder.Append(selfIs).Append(partnerIs);
            builder.Append(" My character speaks in the first person as I and addresses the other character as you.");
            builder.Append(" The message has no asterisks, so your reply must not contain any either, "
                           + "and must not be wrapped in quotation marks that the message does not already have.");
        }
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
        if (hasEmoteMarkup)
        {
            builder.Append(" Decide the following from the asterisks alone, never from what the message is about, and "
                           + "let nothing above override it. Text wrapped in *asterisks* is narration: write it in third "
                           + "person present tense with an explicit subject, like *She smiles.* rather than *smiles*. "
                           + "Text that is not wrapped in asterisks is the character speaking out loud: render it as "
                           + "first-person spoken English, keeping I as I and you as you, never as narration about the "
                           + "characters. Quotation marks around speech are part of the message: if the player wrote "
                           + "them, your reply keeps them in the same place.");
        }
        else
        {
            // "Write only what she says aloud" used to stand here, which handed
            // the model a pronoun the player may never have chosen.
            builder.Append(" This message is the character speaking out loud, and let nothing above override this: your "
                           + "entire reply is that spoken line and nothing else. No narration, no description of what "
                           + "anyone does, no third-person sentences about the characters, no asterisks. Write only the "
                           + "words my character says aloud.");
        }

        builder.Append(" Spoken lines use everyday contractions such as I'm, don't, can't and you're, the way people "
                       + "actually talk, never stiff textbook phrasing.");

        // Marked as an override, or the no-invention rules above win and the
        // player's own instruction is quietly ignored.
        var extra = tab.RpExtraInstruction.Trim();
        if (extra.Length > 0)
            builder.Append(" The player's own instruction, which takes priority over everything above: ").Append(extra);

        return builder.ToString();
    }
}
