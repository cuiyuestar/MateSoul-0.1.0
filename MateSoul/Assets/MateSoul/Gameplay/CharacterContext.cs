using System;
using System.Collections.Generic;
using System.Linq;

namespace MateSoul.Gameplay
{
    [Serializable]
    public sealed class CharacterIdentityDefinition
    {
        public string name;
        public int age;
        public string gender;
        public int height_cm;
    }

    [Serializable]
    public sealed class PersonalityOptionDefinition
    {
        public string id;
        public string display_name;
        public string description;
    }

    [Serializable]
    public sealed class PersonalityTraitDefinition
    {
        public string id;
        public string display_name;
        public string default_option_id;
        public PersonalityOptionDefinition[] options = Array.Empty<PersonalityOptionDefinition>();
    }

    [Serializable]
    public sealed class CharacterPersonaDefinition
    {
        public int schema_version;
        public string character_id;
        public CharacterIdentityDefinition identity;
        public PersonalityTraitDefinition[] personality_traits =
            Array.Empty<PersonalityTraitDefinition>();
    }

    [Serializable]
    public sealed class PersonalitySelectionEntry
    {
        public string trait_id;
        public string option_id;
    }

    [Serializable]
    public sealed class PersonalitySelectionData
    {
        public PersonalitySelectionEntry[] selections =
            Array.Empty<PersonalitySelectionEntry>();
    }

    [Serializable]
    public sealed class PersonalityTraitSnapshot
    {
        public string id;
        public string display_name;
        public string option_id;
        public string option_name;
        public string description;
    }

    [Serializable]
    public sealed class CharacterPersonaSnapshot
    {
        public int schema_version;
        public string character_id;
        public string name;
        public int age;
        public string gender;
        public int height_cm;
        public PersonalityTraitSnapshot[] personality =
            Array.Empty<PersonalityTraitSnapshot>();
    }

    public sealed class CharacterPersonaState
    {
        public const int SupportedSchemaVersion = 1;
        public const int RequiredAge = 16;
        public const string RequiredGender = "female";
        public const int RequiredHeightCm = 155;

        private static readonly string[] RequiredTraits =
        {
            "extraversion",
            "conscientiousness",
            "openness",
            "agreeableness",
            "emotional_stability"
        };

        private readonly CharacterPersonaDefinition definition;
        private readonly Dictionary<string, PersonalityTraitDefinition> traits;
        private readonly Dictionary<string, string> selections;

        public CharacterPersonaState(CharacterPersonaDefinition persona,
            PersonalitySelectionData saved = null)
        {
            definition = persona ?? throw new ArgumentNullException(nameof(persona));
            traits = Validate(persona);
            selections = traits.Values.ToDictionary(
                trait => trait.id, trait => trait.default_option_id, StringComparer.Ordinal);
            Restore(saved);
        }

        public string CharacterId => definition.character_id;
        public CharacterIdentityDefinition Identity => definition.identity;
        public IReadOnlyList<PersonalityTraitDefinition> Traits => definition.personality_traits;
        public event Action Changed;

        public string SelectedOption(string traitId)
        {
            if (traitId == null || !selections.TryGetValue(traitId, out var optionId))
                throw new ArgumentException($"Unknown personality trait: {traitId}", nameof(traitId));
            return optionId;
        }

        public PersonalityOptionDefinition SelectedOptionDefinition(string traitId)
        {
            var trait = Trait(traitId);
            var optionId = SelectedOption(traitId);
            return trait.options.Single(option =>
                string.Equals(option.id, optionId, StringComparison.Ordinal));
        }

        public void Select(string traitId, string optionId)
        {
            var trait = Trait(traitId);
            if (!trait.options.Any(option =>
                    string.Equals(option.id, optionId, StringComparison.Ordinal)))
                throw new ArgumentException(
                    $"Unknown option '{optionId}' for personality trait '{traitId}'.",
                    nameof(optionId));
            if (selections[traitId] == optionId) return;
            selections[traitId] = optionId;
            Changed?.Invoke();
        }

        public void SelectNext(string traitId)
        {
            var trait = Trait(traitId);
            var current = SelectedOption(traitId);
            var index = Array.FindIndex(trait.options,
                option => string.Equals(option.id, current, StringComparison.Ordinal));
            Select(traitId, trait.options[(index + 1) % trait.options.Length].id);
        }

        public PersonalitySelectionData CaptureSelection()
        {
            return new PersonalitySelectionData
            {
                selections = definition.personality_traits.Select(trait =>
                    new PersonalitySelectionEntry
                    {
                        trait_id = trait.id,
                        option_id = selections[trait.id]
                    }).ToArray()
            };
        }

        public CharacterPersonaSnapshot Capture()
        {
            return new CharacterPersonaSnapshot
            {
                schema_version = SupportedSchemaVersion,
                character_id = definition.character_id,
                name = definition.identity.name,
                age = definition.identity.age,
                gender = definition.identity.gender,
                height_cm = definition.identity.height_cm,
                personality = definition.personality_traits.Select(trait =>
                {
                    var option = SelectedOptionDefinition(trait.id);
                    return new PersonalityTraitSnapshot
                    {
                        id = trait.id,
                        display_name = trait.display_name,
                        option_id = option.id,
                        option_name = option.display_name,
                        description = option.description
                    };
                }).ToArray()
            };
        }

        private void Restore(PersonalitySelectionData saved)
        {
            if (saved?.selections == null) return;
            var restored = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in saved.selections)
            {
                if (entry == null || !restored.Add(entry.trait_id))
                    throw new ArgumentException("Saved personality selections contain a duplicate or empty trait.");
                SelectWithoutNotification(entry.trait_id, entry.option_id);
            }
        }

        private void SelectWithoutNotification(string traitId, string optionId)
        {
            var trait = Trait(traitId);
            if (!trait.options.Any(option =>
                    string.Equals(option.id, optionId, StringComparison.Ordinal)))
                throw new ArgumentException(
                    $"Saved personality option '{optionId}' is not registered for '{traitId}'.");
            selections[traitId] = optionId;
        }

        private PersonalityTraitDefinition Trait(string id)
        {
            if (id == null || !traits.TryGetValue(id, out var trait))
                throw new ArgumentException($"Unknown personality trait: {id}", nameof(id));
            return trait;
        }

        private static Dictionary<string, PersonalityTraitDefinition> Validate(
            CharacterPersonaDefinition persona)
        {
            if (persona.schema_version != SupportedSchemaVersion ||
                string.IsNullOrWhiteSpace(persona.character_id) ||
                persona.identity == null ||
                string.IsNullOrWhiteSpace(persona.identity.name) ||
                persona.identity.age != RequiredAge ||
                persona.identity.gender != RequiredGender ||
                persona.identity.height_cm != RequiredHeightCm)
                throw new ArgumentException("Character persona identity or schema is invalid.");
            if (persona.personality_traits == null ||
                persona.personality_traits.Length != RequiredTraits.Length)
                throw new ArgumentException("Character persona must define the five personality traits.");

            var result = new Dictionary<string, PersonalityTraitDefinition>(StringComparer.Ordinal);
            foreach (var trait in persona.personality_traits)
            {
                if (trait == null || string.IsNullOrWhiteSpace(trait.id) ||
                    string.IsNullOrWhiteSpace(trait.display_name) ||
                    trait.options == null || trait.options.Length == 0 ||
                    result.ContainsKey(trait.id))
                    throw new ArgumentException("Character persona contains an invalid personality trait.");
                result.Add(trait.id, trait);
                var optionIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var option in trait.options)
                    if (option == null || string.IsNullOrWhiteSpace(option.id) ||
                        string.IsNullOrWhiteSpace(option.display_name) ||
                        string.IsNullOrWhiteSpace(option.description) ||
                        !optionIds.Add(option.id))
                        throw new ArgumentException(
                            $"Personality trait '{trait.id}' contains an invalid option.");
                if (!optionIds.Contains(trait.default_option_id))
                    throw new ArgumentException(
                        $"Personality trait '{trait.id}' has an unknown default option.");
            }
            if (!RequiredTraits.All(result.ContainsKey))
                throw new ArgumentException("Character persona is missing a required personality trait.");
            return result;
        }
    }

    [Serializable]
    public sealed class RuntimeAppearanceSnapshot
    {
        public string outfit_id;
        public string display_name;
    }

    [Serializable]
    public sealed class RuntimeMoodSnapshot
    {
        public string id;
        public string level;
    }

    [Serializable]
    public sealed class RuntimeActionTargetSnapshot
    {
        public string id;
        public string name;
        public string state;
    }

    [Serializable]
    public sealed class RuntimeActionSnapshot
    {
        public string id;
        public string description;
        public string state;
        public string phase;
        public string initiator;
        public float elapsed_seconds;
        public RuntimeActionTargetSnapshot target;
    }

    [Serializable]
    public sealed class RuntimeEventSnapshot
    {
        public string type;
        public string action_id;
        public string state;
        public string target_id;
        public string target_name;
        public string value;
    }

    [Serializable]
    public sealed class CharacterRuntimeSnapshot
    {
        public int schema_version = 1;
        public long revision;
        public RuntimeAppearanceSnapshot appearance;
        public RuntimeMoodSnapshot mood;
        public string posture;
        public RuntimeActionSnapshot[] actions = Array.Empty<RuntimeActionSnapshot>();
        public RuntimeEventSnapshot[] recent_events = Array.Empty<RuntimeEventSnapshot>();
    }

    [Serializable]
    public sealed class CharacterContextSnapshot
    {
        public CharacterPersonaSnapshot persona;
        public CharacterRuntimeSnapshot runtime;
    }

    public interface ICharacterContextProvider
    {
        CharacterContextSnapshot Capture();
    }
}
