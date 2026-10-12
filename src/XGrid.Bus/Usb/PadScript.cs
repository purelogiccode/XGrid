using System;
using System.Collections.Generic;
using System.Globalization;

namespace XGrid.Bus.Usb;

/// <summary>One parsed <c>--pad-script</c> event: a pad state applied at an exact step.</summary>
public readonly struct PadScriptEvent
{
    /// <summary>Creates a script event from its parts.</summary>
    public PadScriptEvent(PadInput input, ulong step, bool hold)
    {
        Input = input;
        Step = step;
        Hold = hold;
    }

    /// <summary>Gets the pad state applied at <see cref="Step"/>.</summary>
    public PadInput Input { get; }

    /// <summary>Gets the emulated step at which the input is applied.</summary>
    public ulong Step { get; }

    /// <summary>Gets whether the input latches until the next event (no auto-release).</summary>
    public bool Hold { get; }
}

/// <summary>
/// Parsed <c>--pad-script</c> event list (<c>buttonset@step[+hold],...</c>) with
/// step-boundary application, e.g. <c>down@960000000,a@961000000</c>.
/// Momentary presses auto-release after <see cref="DefaultPressSteps"/> steps;
/// <c>+hold</c> events latch until the next event.
/// </summary>
public sealed class PadScript
{
    /// <summary>Default momentary press length in emulated steps.</summary>
    public const ulong DefaultPressSteps = 1_000_000;

    private readonly List<PadScriptEvent> _events;
    private int _nextIndex;
    private ulong _releaseStep;
    private bool _hasRelease;

    private PadScript(List<PadScriptEvent> events)
    {
        _events = events;
    }

    /// <summary>Gets the number of parsed events.</summary>
    public int Count
    {
        get { return _events.Count; }
    }

    /// <summary>Gets the parsed events in application order (stable by step).</summary>
    public IReadOnlyList<PadScriptEvent> Events
    {
        get { return _events; }
    }

    /// <summary>Parses a script spec; null or empty yields a script with no events.</summary>
    public static bool TryParse(string? spec, out PadScript? script)
    {
        script = null;
        var ordered = new List<(PadInput Input, ulong Step, bool Hold, int Order)>();
        if (!string.IsNullOrWhiteSpace(spec))
        {
            string[] raws = spec.Split(',');
            for (int i = 0; i < raws.Length; i++)
            {
                string raw = raws[i].Trim();
                if (raw.Length == 0)
                {
                    return false;
                }

                bool hold = false;
                if (raw.EndsWith("+hold", StringComparison.OrdinalIgnoreCase))
                {
                    hold = true;
                    raw = raw[..^5].TrimEnd();
                }

                string[] sides = raw.Split('@');
                if (sides.Length != 2 || sides[0].Trim().Length == 0)
                {
                    return false;
                }

                if (!ulong.TryParse(sides[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong step))
                {
                    return false;
                }

                if (!PadInput.TryParseButtonSet(sides[0].Trim(), out ushort buttons, out byte[] analog))
                {
                    return false;
                }

                ordered.Add((new PadInput(buttons, analog, new short[4]), step, hold, i));
            }

            ordered.Sort(static (a, b) =>
                a.Step != b.Step ? a.Step.CompareTo(b.Step) : a.Order.CompareTo(b.Order));
        }

        var events = new List<PadScriptEvent>(ordered.Count);
        foreach ((PadInput input, ulong step, bool hold, _) in ordered)
        {
            events.Add(new PadScriptEvent(input, step, hold));
        }

        script = new PadScript(events);
        return true;
    }

    /// <summary>Restarts event application from the first event.</summary>
    public void Reset()
    {
        _nextIndex = 0;
        _hasRelease = false;
        _releaseStep = 0;
    }

    /// <summary>
    /// Applies due events at the given step: consumes every event at or below it
    /// (last wins) and auto-releases an expired momentary press.
    /// </summary>
    /// <returns>Whether the pad state changed and must be latched into the gamepad.</returns>
    public bool ApplyIfDue(ulong step, out PadInput pressed)
    {
        pressed = PadInput.Neutral;
        bool applied = false;
        while (_nextIndex < _events.Count && _events[_nextIndex].Step <= step)
        {
            pressed = _events[_nextIndex].Input;
            if (_events[_nextIndex].Hold)
            {
                _hasRelease = false;
            }
            else
            {
                _releaseStep = _events[_nextIndex].Step > ulong.MaxValue - DefaultPressSteps
                    ? ulong.MaxValue
                    : _events[_nextIndex].Step + DefaultPressSteps;
                _hasRelease = true;
            }

            _nextIndex++;
            applied = true;
        }

        if (!applied && _hasRelease && step >= _releaseStep)
        {
            pressed = PadInput.Neutral;
            _hasRelease = false;
            applied = true;
        }

        return applied;
    }

    /// <summary>Gets the next press/release step strictly above the given step.</summary>
    public bool TryGetNextBoundary(ulong step, out ulong boundary)
    {
        boundary = 0;
        bool found = false;
        if (_nextIndex < _events.Count && _events[_nextIndex].Step > step)
        {
            boundary = _events[_nextIndex].Step;
            found = true;
        }

        if (_hasRelease && _releaseStep > step && (!found || _releaseStep < boundary))
        {
            boundary = _releaseStep;
            found = true;
        }

        return found;
    }
}
