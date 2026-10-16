namespace Rts.Game;

/// <summary>A Keybinds tab text from <c>ui.json</c> <c>keys</c> (M6-V8); the key is the member's snake_case name (<see cref="UiText.Key{T}"/>), which for the order, Tab and build members is also the input action's name.</summary>
public enum KeysText
{
    /// <summary>The settings screen's first tab (the M6-V6 fields).</summary>
    TabGeneral,
    /// <summary>The settings screen's Keybinds tab.</summary>
    TabKeybinds,
    /// <summary>Attack-move (A).</summary>
    OrderAttackMove,
    /// <summary>Stop (S).</summary>
    OrderStop,
    /// <summary>Hold position (H).</summary>
    OrderHold,
    /// <summary>Move (M).</summary>
    OrderMove,
    /// <summary>The next Tab subgroup.</summary>
    SubgroupNext,
    /// <summary>The worker's basic build menu (B).</summary>
    BuildBasic,
    /// <summary>The worker's advanced build menu (V).</summary>
    BuildAdvanced,
    /// <summary>A control group's key: a format whose <c>{0}</c> is the group number (1-9).</summary>
    Group,
    /// <summary>A command card grid key: a format whose <c>{0}</c> is the row (1-3) and <c>{1}</c> the column (1-5).</summary>
    Card,
    /// <summary>What a key button reads while it waits for a key.</summary>
    PressKey,
    /// <summary>The refusal of a key another action holds: a format whose <c>{0}</c> is the key and <c>{1}</c> that action's name.</summary>
    Conflict,
    /// <summary>The refusal of a fixed key (Esc, a modifier, an arrow, F10, F12): a format whose <c>{0}</c> is the key.</summary>
    Reserved,
    /// <summary>The tab's button that puts every key back to its default (on screen; Apply or Back saves it).</summary>
    Defaults,
}
