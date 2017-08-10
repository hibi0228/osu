﻿// Copyright (c) 2007-2017 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu/master/LICENCE

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Input;
using osu.Game.Rulesets;

namespace osu.Game.Input
{
    public enum ConcurrentActionMode
    {
        /// <summary>
        /// One action can be actuated at once. The first action matching a chord will take precedence and no other action will be actuated until it has been released.
        /// </summary>
        None,
        /// <summary>
        /// Unique actions are allowed to be fired at the same time. There may therefore be more than one action in an actuated state at once.
        /// If one action has multiple bindings, only the first will add actuation data, and the last to be released will add de-actuation data.
        /// </summary>
        UniqueActions,
        /// <summary>
        /// Both unique actions and the same action can be concurrently actuated.
        /// Same as <see cref="UniqueActions"/>, but multiple bindings for the same action will individually add actuation and de-actuation data to events.
        /// </summary>
        UniqueAndSameActions,
    }


    /// <summary>
    /// Maps custom action data of type <see cref="T"/> and stores to <see cref="InputState.Data"/>.
    /// </summary>
    /// <typeparam name="T">The type of the custom action.</typeparam>
    public abstract class ActionMappingInputManager<T> : PassThroughInputManager
        where T : struct
    {
        private readonly RulesetInfo ruleset;

        private readonly int? variant;

        private readonly ConcurrentActionMode concurrencyMode;

        private readonly List<Binding> mappings = new List<Binding>();

        /// <summary>
        /// Create a new instance.
        /// </summary>
        /// <param name="ruleset">A reference to identify the current <see cref="Ruleset"/>. Used to lookup mappings. Null for global mappings.</param>
        /// <param name="variant">An optional variant for the specified <see cref="Ruleset"/>. Used when a ruleset has more than one possible keyboard layouts.</param>
        /// <param name="concurrencyMode">Specify how to deal with multiple matches of combinations and actions.</param>
        protected ActionMappingInputManager(RulesetInfo ruleset = null, int? variant = null, ConcurrentActionMode concurrencyMode = ConcurrentActionMode.None)
        {
            this.ruleset = ruleset;
            this.variant = variant;
            this.concurrencyMode = concurrencyMode;
        }

        protected abstract IDictionary<KeyCombination, T> CreateDefaultMappings();

        private BindingStore store;

        [BackgroundDependencyLoader]
        private void load(BindingStore bindings)
        {
            store = bindings;
            ReloadMappings();
        }

        protected void ReloadMappings()
        {
            var rulesetId = ruleset?.ID;

            mappings.Clear();

            foreach (var kvp in CreateDefaultMappings())
                mappings.Add(new Binding(kvp.Key, kvp.Value));

            if (store != null)
            {
                foreach (var b in store.Query<Binding>(b => b.RulesetID == rulesetId && b.Variant == variant))
                    mappings.Add(b);
            }

            if (concurrencyMode > ConcurrentActionMode.None)
            {
                // ensure we have no overlapping bindings.
                foreach (var m in mappings)
                    foreach (var colliding in mappings.Where(k => !k.Keys.Equals(m.Keys) && k.Keys.CheckValid(m.Keys.Keys)))
                        throw new InvalidOperationException($"Multiple partially overlapping bindings are not supported ({m} and {colliding} are colliding)!");
            }
        }

        private readonly List<Binding> pressedBindings = new List<Binding>();

        protected override void PopulateDataKeyDown(InputState state, KeyDownEventArgs args)
        {
            if (!args.Repeat && (concurrencyMode > ConcurrentActionMode.None || pressedBindings.Count == 0))
            {
                Binding validBinding;

                if ((validBinding = mappings.Except(pressedBindings).LastOrDefault(m => m.Keys.CheckValid(state.Keyboard.Keys, concurrencyMode == ConcurrentActionMode.None))) != null)
                {
                    if (concurrencyMode == ConcurrentActionMode.UniqueAndSameActions || pressedBindings.All(p => p.Action != validBinding.Action))
                        state.Data = validBinding.GetAction<T>();

                    // store both the pressed combination and the resulting action, just in case the assignments change while we are actuated.
                    pressedBindings.Add(validBinding);
                }
            }
        }

        protected override void PopulateDataKeyUp(InputState state, KeyUpEventArgs args)
        {
            foreach (var binding in pressedBindings.ToList())
            {
                if (!binding.Keys.CheckValid(state.Keyboard.Keys, concurrencyMode == ConcurrentActionMode.None))
                {
                    // clear the no-longer-valid combination/action.
                    pressedBindings.Remove(binding);

                    if (concurrencyMode == ConcurrentActionMode.UniqueAndSameActions || pressedBindings.All(p => p.Action != binding.Action))
                        // set data as KeyUp if we're all done with this action.
                        state.Data = binding.GetAction<T>();
                }
            }
        }
    }
}
