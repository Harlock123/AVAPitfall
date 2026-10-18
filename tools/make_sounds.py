#!/usr/bin/env python3
"""Synthesises AVAPitfall's sound effects and tunes into ../src/AVAPitfall/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle, sine and noise voices. The jungle
drums and the marimba tunes are new compositions. Harry's yell on the vine is made by the formant voice in speech.py. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAPitfall", "Sounds")
RNG = random.Random(1984)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def drum(seconds, f0, f1, gain=1.0):
    """A hand drum: a falling sine with a slap of noise."""
    return mix(tone(seconds, f0, f1, "sine", attack=0.002, release=1.4, gain=gain), tone(0.03, 1800, 600, "noise", attack=0.001, release=1.0, gain=gain * 0.4))


def marimba(note, seconds, gain=0.6):
    f = hz(note)
    return mix(tone(seconds, f, kind="sine", attack=0.003, release=2.2, gain=gain), tone(seconds * 0.4, f * 4, kind="sine", attack=0.002, release=2.5, gain=gain * 0.25))


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    write("Jump.wav", tone(0.18, 300, 620, "square", attack=0.002, release=0.6, gain=0.5), 0.12)
    write("Land.wav", tone(0.05, 160, 80, "square", attack=0.001, release=1.2, gain=0.5), 0.1)
    write("Screen.wav", tone(0.02, 900, 1000, "square", attack=0.001, release=1.0, gain=0.3), 0.05)
    write("Treasure.wav", cat(*[tone(0.05, hz(n), kind="pulse", release=0.3) for n in ["E6", "G6", "B6", "E7", "G7"]], tone(0.25, hz("B7"), kind="pulse", vibrato=0.02, release=0.6)), 0.26)
    write("Log.wav", mix(tone(0.12, 140, 70, "sine", attack=0.001, release=1.0), tone(0.06, 500, 200, "noise", release=1.0, gain=0.5)), 0.3)
    write("Fall.wav", tone(0.6, 900, 140, "square", attack=0.002, release=0.3, gain=0.5), 0.18)
    write("Sink.wav", cat(tone(0.25, 300, 150, "noise", attack=0.01, release=0.6, gain=0.6), tone(0.7, 180, 60, "sine", release=1.0, vibrato=0.1, vib_rate=12)), 0.3)
    write("Die.wav", cat(*[tone(0.04, 1300 - i * 50, kind="square", release=0.2, gain=0.6) for i in range(22)]), 0.3)
    write("Tick.wav", tone(0.025, 1800, 1200, "square", attack=0.001, release=1.0, gain=0.5), 0.12)
    # The swing: a yodelling yell, up and down and up again.
    yell = say("AA1 AA1 IY1 AA1 IY1 AA1 !", rate=RATE, pitch=170, speed=1.1, size=1.0, bend=lambda t: 1.0 + 0.5 * abs(math.sin(t * math.pi * 3)))
    write("Yell.wav", yell, 0.45)

    # In the jungle (an original composition): drums, and a marimba over them now and then.
    beat = 60 / 120
    bar = cat(drum(beat * 0.5, 160, 70), drum(beat * 0.5, 220, 120, 0.5), drum(beat * 0.5, 160, 70, 0.8), drum(beat * 0.5, 300, 180, 0.6),
              drum(beat * 0.5, 160, 70), drum(beat * 0.5, 220, 120, 0.5), drum(beat * 0.25, 300, 180, 0.6), drum(beat * 0.25, 300, 180, 0.6), drum(beat * 0.5, 120, 60, 0.9))
    drums = cat(*[bar] * 8)
    tune_notes = [("A4", 1), ("C5", 0.5), ("D5", 0.5), ("E5", 1), ("D5", 0.5), ("C5", 0.5), ("A4", 2), ("-", 2),
                  ("G4", 1), ("A4", 0.5), ("C5", 0.5), ("D5", 1), ("C5", 0.5), ("A4", 0.5), ("G4", 2), ("-", 2),
                  ("A4", 1), ("C5", 0.5), ("D5", 0.5), ("E5", 1), ("G5", 0.5), ("E5", 0.5), ("D5", 2), ("C5", 1), ("A4", 1),
                  ("C5", 1), ("A4", 0.5), ("G4", 0.5), ("A4", 4)]
    mel = []
    for n, b in tune_notes:
        mel += silence(b * beat) if n == "-" else marimba(n, b * beat, 0.35)
    write("Music.wav", mix(drums, mel), 0.3)
    title_notes = [("E5", 0.5), ("G5", 0.5), ("A5", 1), ("G5", 0.5), ("E5", 0.5), ("D5", 1), ("E5", 0.5), ("D5", 0.5), ("C5", 1), ("A4", 2),
                   ("C5", 0.5), ("D5", 0.5), ("E5", 1), ("G5", 1), ("A5", 0.5), ("G5", 0.5), ("E5", 1), ("D5", 4)]
    tm = []
    for n, b in title_notes:
        tm += marimba(n, b * beat, 0.5)
    write("TitleTune.wav", cat(mix(cat(*[bar] * 4), tm), silence(1.0)), 0.34)
    write("Victory.wav", tune(150, [("A5", 0.5), ("C6", 0.5), ("E6", 0.5), ("A6", 1.5), ("G6", 0.5), ("A6", 0.5), ("B6", 0.5), ("C7", 3)],
                              [("A3", 1), ("E3", 1), ("F3", 1), ("D3", 1), ("E3", 1), ("A2", 2.5)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(70, [("E5", 1), ("C5", 1), ("A4", 1), ("E4", 1), ("A4", 3)], [("A2", 2), ("F2", 2), ("A1", 3)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
