// Copyright (c) 2026 AdvanGeneration Pty. Ltd.
// Licensed under the MIT License.
// See LICENSE.txt for license information.

namespace AdvGenNoSqlServer.Storage.FullText;

/// <summary>
/// Defines the contract for word stemming algorithms
/// Reduces words to their root/base form
/// </summary>
public interface IStemmer
{
    /// <summary>
    /// Stems a word to its root form
    /// </summary>
    /// <param name="word">The word to stem</param>
    /// <returns>The stemmed word</returns>
    string Stem(string word);

    /// <summary>
    /// Gets the stemmer name
    /// </summary>
    string Name { get; }
}

/// <summary>
/// Porter Stemmer implementation based on the Porter Stemming Algorithm
/// Reduces English words to their word stem (base form)
/// </summary>
public class PorterStemmer : IStemmer
{
    /// <inheritdoc />
    public string Name => "Porter";

    /// <inheritdoc />
    public string Stem(string word)
    {
        if (string.IsNullOrEmpty(word))
            return word;

        word = word.ToLowerInvariant();

        if (word.Length <= 2)
            return word;

        string stem = word;

        // Step 1a: plurals and -ed/-ing
        stem = Step1a(stem);
        // Step 1b: -ed, -ing
        stem = Step1b(stem);
        // Step 1c: -y
        stem = Step1c(stem);
        // Step 2: double suffixes
        stem = Step2(stem);
        // Step 3: -ic-, -full, -ness, etc.
        stem = Step3(stem);
        // Step 4: -ant, -ence, etc.
        stem = Step4(stem);
        // Step 5a: -e
        stem = Step5a(stem);
        // Step 5b: double consonant
        stem = Step5b(stem);

        return stem;
    }

    private static string Step1a(string word)
    {
        // sses -> ss
        if (word.EndsWith("sses"))
            return word[..^2];
        // ies -> i
        if (word.EndsWith("ies"))
            return word[..^2];
        // ss -> ss (no change)
        if (word.EndsWith("ss"))
            return word;
        // s -> (remove)
        if (word.EndsWith("s") && word.Length > 2)
            return word[..^1];
        return word;
    }

    private static string Step1b(string word)
    {
        // eed -> ee (if measure > 0)
        if (word.EndsWith("eed"))
        {
            string stem = word[..^3];
            if (Measure(stem) > 0)
                return stem + "ee";
            return word;
        }

        // ed (if contains vowel)
        if (word.EndsWith("ed"))
        {
            string stem = word[..^2];
            if (HasVowel(stem))
            {
                stem = RemoveDoubleConsonant(stem);
                if (EndsWith(stem, "at") || EndsWith(stem, "bl") || EndsWith(stem, "iz"))
                    return stem + "e";
                return stem;
            }
            return word;
        }

        // ing (if contains vowel)
        if (word.EndsWith("ing"))
        {
            string stem = word[..^3];
            if (HasVowel(stem))
            {
                stem = RemoveDoubleConsonant(stem);
                if (EndsWith(stem, "at") || EndsWith(stem, "bl") || EndsWith(stem, "iz"))
                    return stem + "e";
                return stem;
            }
            return word;
        }

        return word;
    }

    private static string Step1c(string word)
    {
        // y -> i (if vowel in stem)
        if (word.EndsWith("y") && word.Length > 2)
        {
            string stem = word[..^1];
            if (HasVowel(stem))
                return stem + "i";
        }
        return word;
    }

    private static string Step2(string word)
    {
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ational"] = "ate",
            ["tional"] = "tion",
            ["enci"] = "ence",
            ["anci"] = "ance",
            ["izer"] = "ize",
            ["abli"] = "able",
            ["alli"] = "al",
            ["entli"] = "ent",
            ["eli"] = "e",
            ["ousli"] = "ous",
            ["ization"] = "ize",
            ["ation"] = "ate",
            ["ator"] = "ate",
            ["alism"] = "al",
            ["iveness"] = "ive",
            ["fulness"] = "ful",
            ["ousness"] = "ous",
            ["aliti"] = "al",
            ["iviti"] = "ive",
            ["biliti"] = "ble"
        };

        foreach (var (suffix, replacement) in replacements)
        {
            if (word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                string stem = word[..^suffix.Length];
                if (Measure(stem) > 0)
                    return stem + replacement;
                break;
            }
        }
        return word;
    }

    private static string Step3(string word)
    {
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["icate"] = "ic",
            ["ative"] = "",
            ["alize"] = "al",
            ["iciti"] = "ic",
            ["ical"] = "ic",
            ["ful"] = "",
            ["ness"] = ""
        };

        foreach (var (suffix, replacement) in replacements)
        {
            if (word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                string stem = word[..^suffix.Length];
                if (Measure(stem) > 0)
                    return stem + replacement;
                break;
            }
        }
        return word;
    }

    private static string Step4(string word)
    {
        string[] suffixes = { "al", "ance", "ence", "er", "ic", "able", "ible", "ant", "ement", 
                              "ment", "ent", "ion", "ou", "ism", "ate", "iti", "ous", "ive", "ize" };

        foreach (var suffix in suffixes)
        {
            if (word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                string stem = word[..^suffix.Length];
                // Special case for -ion: must be preceded by s or t
                if (suffix == "ion" && stem.Length > 0)
                {
                    char last = stem[^1];
                    if (last != 's' && last != 't')
                        continue;
                }
                if (Measure(stem) > 1)
                    return stem;
                break;
            }
        }
        return word;
    }

    private static string Step5a(string word)
    {
        // e -> (remove) if measure > 1, or (measure == 1 and not cvc)
        if (word.EndsWith("e"))
        {
            string stem = word[..^1];
            int m = Measure(stem);
            if (m > 1 || (m == 1 && !IsCvc(stem)))
                return stem;
        }
        return word;
    }

    private static string Step5b(string word)
    {
        // ll -> l if measure > 1
        if (word.EndsWith("ll") && Measure(word) > 1)
            return word[..^1];
        return word;
    }

    private static bool HasVowel(string word)
    {
        return word.Any(IsVowel);
    }

    private static bool IsVowel(char c)
    {
        return "aeiou".Contains(char.ToLower(c));
    }

    private static bool IsConsonant(char c)
    {
        return char.IsLetter(c) && !IsVowel(c);
    }

    private static bool IsCvc(string word)
    {
        if (word.Length < 3) return false;
        int len = word.Length;
        return IsConsonant(word[len - 3]) && IsVowel(word[len - 2]) && IsConsonant(word[len - 1])
            && !"wxy".Contains(word[len - 1]);
    }

    private static bool EndsWith(string word, string suffix)
    {
        return word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static string RemoveDoubleConsonant(string word)
    {
        if (word.Length >= 2)
        {
            char last = word[^1];
            char secondLast = word[^2];
            if (last == secondLast && IsConsonant(last))
            {
                // Don't remove if it's l, s, or z (special case)
                if (last != 'l' && last != 's' && last != 'z')
                    return word[..^1];
            }
        }
        return word;
    }

    /// <summary>
    /// Calculates the measure of a word (number of VC patterns)
    /// </summary>
    private static int Measure(string word)
    {
        if (string.IsNullOrEmpty(word)) return 0;

        int measure = 0;
        bool hasVowel = false;

        foreach (char c in word)
        {
            if (IsVowel(c))
            {
                hasVowel = true;
            }
            else if (hasVowel)
            {
                measure++;
                hasVowel = false;
            }
        }

        return measure;
    }
}

/// <summary>
/// Identity stemmer that returns words unchanged
/// Useful for languages where stemming is not needed or for exact matching
/// </summary>
public class IdentityStemmer : IStemmer
{
    /// <inheritdoc />
    public string Name => "Identity";

    /// <inheritdoc />
    public string Stem(string word) => word?.ToLowerInvariant() ?? string.Empty;
}
