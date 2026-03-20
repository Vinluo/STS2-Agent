# Instant Mode Runtime Findings (2026-03-20)

Scope: live run on a manually launched game instance, starting from `MAIN_MENU`, then entering a fresh Ironclad run and continuing through floor 3 with `mode=instant` unless noted otherwise.

Current run state when this note was written:
- `run_id=ZVQYSD10H8`
- `screen=MAP`
- `floor=3`
- `available_actions=["choose_map_node"]`

## Hard blockers

### 1. `collect_rewards_and_proceed(mode=instant)` can hard-stick on `REWARD`

Observed state:
- Reward contents were already fully consumed.
- `reward.can_proceed=true`
- `reward.rewards=[]`
- `available_actions=["collect_rewards_and_proceed"]`

Observed action result:
- `status="pending"`
- `stable=false`
- response `screen="REWARD"`
- response `available_actions=["collect_rewards_and_proceed"]`

Observed follow-up:
- Repeated polling stayed on the same reward screen and never left.
- The same action in `mode=stable` completed immediately and moved the run to `MAP`.

Impact:
- This is a real progression blocker for instant-only agents.

Suggested direction:
- Add a reward-specific fast path that treats `can_proceed=true` plus empty rewards as a deterministic proceed condition.
- If the underlying UI still needs a frame, keep waiting until `screen != REWARD` or `choose_map_node` becomes available.

### 2. Finished events still expose `choose_event_option` instead of a dedicated proceed action

Observed in two places:
- Neow event after the relic reward was already applied.
- `ROOM_FULL_OF_CHEESE` after the second card pick finished the event.

Observed state shape:
- `event.is_finished=true`
- only one proceed-like option remained
- `available_actions=["choose_event_option"]`

Impact:
- The client cannot tell whether it is choosing a real event branch or just clicking a post-event proceed.
- Tooling and validation must special-case finished events.

Suggested direction:
- Expose `proceed` once an event is finished.
- Keep `choose_event_option` only for unresolved branches.

## Repeated rough edges

### 3. `choose_map_node(mode=instant)` can report a transient destination that is not the final room

Observed case:
- From floor 2 map, choosing `Unknown` returned a response with `screen="COMBAT"` and no actions.
- Shortly after, the actual stable state became `EVENT` (`ROOM_FULL_OF_CHEESE`).

Impact:
- The response payload is not a reliable prediction of the eventual room type.
- A caller that branches immediately on response `screen` can make the wrong next move.

Suggested direction:
- For map travel in instant mode, keep waiting until the room type is stable enough to expose the next actionable state.
- Alternatively add a `transition_state`/`state` split everywhere at the raw API layer, not only in MCP.

### 4. Combat actions still pass through empty-action transition windows

Observed case:
- `end_turn(mode=instant)` completed immediately.
- The next observed state was still `screen="COMBAT"` but `available_actions=[]`.
- Only later did the game become actionable again at the next player turn.

Impact:
- An agent that only checks `screen=="COMBAT"` will overrun enemy-turn transitions.

Suggested direction:
- Instant combat actions should wait until either combat ends or player actions become available again.
- At minimum, surface a clearer transition marker in the response.

### 5. Chaining `play_card(mode=instant)` without waiting for a real state change can hit index drift / conflicts

Observed case:
- During the first fight, aggressive chaining on stale hand state eventually hit `HTTP 409 Conflict`.
- After switching to a stricter wait rule ("energy/hand/enemy HP actually changed"), combat continued cleanly.

Impact:
- Hand indexes are not safe to reuse from a pending response unless the caller re-reads stable state.

Suggested direction:
- For combat card play, instant mode should settle until hand/energy/enemy HP changes, not just until the request is accepted.

### 6. Multi-pick card-selection metadata is inconsistent

Observed in `ROOM_FULL_OF_CHEESE`:
- Prompt said "choose 2 cards".
- `selection.min_select=0`
- `selection.max_select=0`
- `selection.selected_count=0`
- `available_actions=["select_deck_card"]`

Observed follow-up:
- First `select_deck_card(mode=instant)` kept the screen on `CARD_SELECTION`.
- Second `select_deck_card(mode=instant)` finished the event and returned to finished-event proceed state.

Impact:
- The metadata does not tell the caller how many picks remain.
- The only reliable signal is trial-and-error via repeated selection.

Suggested direction:
- Fix `min_select`, `max_select`, and `selected_count`.
- If the selection is multi-step, expose remaining picks explicitly.

### 7. Some instant responses return the same screen/action snapshot even though game state has already changed underneath

Observed examples:
- Neow reward choice returned `pending` with `screen="EVENT"` and `available_actions=["choose_event_option"]`, while the relic and card had already been granted.
- Gold reward claim returned `pending` with the same reward action set, but gold had already increased from `99` to `109`.

Impact:
- A pure response-driven caller misses progress unless it polls state again.

Suggested direction:
- Prefer returning either the first changed state or both:
  - `transition_state`
  - `settled_state`

## Useful workarounds observed during the run

- `collect_rewards_and_proceed` was recoverable by retrying the same action in `mode=stable`.
- Combat became reliable once the caller waited for a real state delta after each `play_card`.
- Finished events were still navigable by reusing `choose_event_option` on the proceed entry, even though that is semantically awkward.

## Optimization priority

1. Fix `collect_rewards_and_proceed(mode=instant)` reward-screen deadlock.
2. Expose a dedicated `proceed` action for finished events.
3. Make instant map travel and combat actions settle to a truly actionable state.
4. Correct card-selection metadata for multi-pick flows.
