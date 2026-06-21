using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewLivingValley.Models;

namespace StardewLivingValley.Brain
{
    public class ConversationPlaybackManager
    {
        private class ActiveConversation
        {
            public NPC NpcA { get; set; }
            public NPC NpcB { get; set; }
            public List<ConversationLine> Script { get; set; }
            public int CurrentLineIndex { get; set; }
            public int TicksUntilNextLine { get; set; }

            public NPC GetNpcByName(string name)
            {
                if (NpcA != null && NpcA.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return NpcA;
                if (NpcB != null && NpcB.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return NpcB;
                return null;
            }
        }

        private readonly IModHelper _helper;
        private readonly IMonitor _monitor;
        private readonly List<ActiveConversation> _activeConversations = new List<ActiveConversation>();

        public ConversationPlaybackManager(IModHelper helper, IMonitor monitor)
        {
            _helper = helper;
            _monitor = monitor;
            _helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            _helper.Events.Input.ButtonPressed += OnButtonPressed;
        }

        public bool IsNpcBusy(string npcName)
        {
            return _activeConversations.Any(c => 
                (c.NpcA != null && c.NpcA.Name == npcName) || 
                (c.NpcB != null && c.NpcB.Name == npcName));
        }

        public void StartPlayback(NPC npcA, NPC npcB, List<ConversationLine> script)
        {
            if (script == null || script.Count == 0) return;

            // Prevent starting a new playback if already busy
            if (IsNpcBusy(npcA.Name) || IsNpcBusy(npcB.Name)) return;

            var session = new ActiveConversation
            {
                NpcA = npcA,
                NpcB = npcB,
                Script = script,
                CurrentLineIndex = 0,
                TicksUntilNextLine = 0
            };

            _activeConversations.Add(session);

            // Freeze motion so they don't walk away during chat
            SetFreezeMotion(npcA, true);
            SetFreezeMotion(npcB, true);
            
            // Make them face each other
            npcA.faceGeneralDirection(npcB.Position);
            npcB.faceGeneralDirection(npcA.Position);
            
            _monitor.Log($"[Playback] Starting conversation between {npcA.Name} and {npcB.Name} ({script.Count} lines)", LogLevel.Info);
        }

        public void Abort(NPC npc)
        {
            var session = _activeConversations.FirstOrDefault(c => 
                (c.NpcA != null && c.NpcA == npc) || 
                (c.NpcB != null && c.NpcB == npc));

            if (session != null)
            {
                AbortSession(session);
            }
        }

        private void AbortSession(ActiveConversation session)
        {
            _monitor.Log($"[Playback] Conversation between {session.NpcA?.Name} and {session.NpcB?.Name} aborted.", LogLevel.Info);
            
            if (session.NpcA != null) SetFreezeMotion(session.NpcA, false);
            if (session.NpcB != null) SetFreezeMotion(session.NpcB, false);

            _activeConversations.Remove(session);
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (_activeConversations.Count == 0) return;

            // Iterate backwards to allow safe removal during iteration
            for (int i = _activeConversations.Count - 1; i >= 0; i--)
            {
                var session = _activeConversations[i];

                if (session.TicksUntilNextLine > 0)
                {
                    session.TicksUntilNextLine--;
                    continue;
                }

                if (session.CurrentLineIndex < session.Script.Count)
                {
                    var line = session.Script[session.CurrentLineIndex];
                    NPC speaker = session.GetNpcByName(line.Speaker);
                    
                    if (speaker != null)
                    {
                        speaker.showTextAboveHead(line.Text);
                        // Make the speaker face the other npc just in case they moved
                        NPC other = (speaker == session.NpcA) ? session.NpcB : session.NpcA;
                        speaker.faceGeneralDirection(other.Position);

                        session.TicksUntilNextLine = 60 + (line.Text.Length * 3) + 30;
                    }
                    else
                    {
                        session.TicksUntilNextLine = 30;
                    }
                    
                    session.CurrentLineIndex++;
                }
                else
                {
                    // Finished
                    AbortSession(session);
                }
            }
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (_activeConversations.Count == 0) return;

            if (e.Button.IsActionButton())
            {
                // Check if player clicked any NPC in an active conversation
                for (int i = _activeConversations.Count - 1; i >= 0; i--)
                {
                    var session = _activeConversations[i];
                    if ((session.NpcA != null && Game1.currentCursorTile == session.NpcA.Tile) ||
                        (session.NpcB != null && Game1.currentCursorTile == session.NpcB.Tile))
                    {
                        AbortSession(session);
                    }
                }
            }
        }

        private void SetFreezeMotion(NPC npc, bool freeze)
        {
            try
            {
                _helper.Reflection.GetField<bool>(npc, "freezeMotion").SetValue(freeze);
            }
            catch (Exception) { /* Ignorar si falla */ }
        }
    }
}
