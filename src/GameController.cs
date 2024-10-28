﻿using System;
using System.Linq;
using FCAP.AI;
using FCAP.Graphics;
using UnityEngine;
using static FCAP.Enums;

namespace FCAP
{
    public class GameController : UpdatableAndDeletable
    {
        public static GameController Instance;
        public static Animatronic LastJumpscare = Animatronic.None;

        public int Power = Constants.MaxPower + 200; // 200 is a safety net so power doesn't go down for 5 seconds
        public int PowerUsage = 0;

        public int Hour = 0;
        public int OOPTimer = 0;

        public Map.Location CamViewing = Map.Location.ShowStage;
        public Map.Location CamSelected = Map.Location.ShowStage;
        public bool InCams = false;
        public int CamViewTimer = 0;

        public bool LeftDoorLight = false;
        public bool LeftDoorShut = false;
        public bool RightDoorLight = false;
        public bool RightDoorShut = false;
        public int LeftDoorLightCounter = 0;
        public int RightDoorLightCounter = 0;

        public Animatronic CurrentJumpscare = Animatronic.None;
        public int JumpscareTimer = 0;
        public Jumpscare jumpscareObj = null;

        public Overseer camsOverseer = null;
        public Overseer lDoorOverseer = null;
        public Overseer rDoorOverseer = null;

        public MapDisplay mapDisplay = null;
        public PowerDisplay powerDisplay = null;
        public PhonePebbles phoneGuy = null;

        public bool OutOfPower => Power <= 0;

        public BaseAI[] AIs;
        public GameController(Room room)
        {
            Plugin.Logger.LogDebug("Activated :)");
            this.room = room;
            Instance = this;
            int cycle = Math.Min(6, room.game.GetStorySession.saveState.cycleNumber);
            AIs = [
                new SurvivorAI(this, cycle),
                new MonkAI(this, cycle),
                new HunterAI(this, cycle),
                new NightcatAI(this, cycle)
            ];

            mapDisplay = new MapDisplay(this, room);
            powerDisplay = new PowerDisplay(this, room);
            phoneGuy = new PhonePebbles(this, room, room.game.GetStorySession.saveState.cycleNumber);
        }

        public override void Update(bool eu)
        {
            base.Update(eu);

            // Game complete?
            if (room.world.rainCycle.timer > 40 * 60 * 6)
            {
                if (room.game.manager.upcomingProcess == null)
                {
                    if (room.game.GetStorySession.saveState.cycleNumber == 4)
                    {
                        // Night 5, beating the game, roll credits
                        RainWorldGame.BeatGameMode(room.game, true); // for the sake of save file simplicity, we assume that we ascended
                        room.game.ExitGame(false, false);
                        room.game.manager.RequestMainProcessSwitch(Constants.WeekOverScreen, 0f);
                    }
                    else
                    {
                        // Beating like normal
                        room.game.GetStorySession.AppendTimeOnCycleEnd(true);
                        room.game.ExitGame(false, false);
                        room.game.GetStorySession.saveState.SessionEnded(room.game, true, false);
                        room.game.manager.RequestMainProcessSwitch(Constants.NightOverScreen, 0f);
                    }
                }
                return;
            }

            // Power
            if (!OutOfPower)
            {
                PowerUsage = 1 + (LeftDoorLight ? 1 : 0) + (LeftDoorShut ? 1 : 0) + (RightDoorLight ? 1 : 0) + (RightDoorShut ? 1 : 0) + (InCams ? 1 : 0);
                Power -= PowerUsage;
            }

            if (OutOfPower)
            {
                InCams = false;
                LeftDoorLight = false;
                LeftDoorShut = false;
                RightDoorLight = false;
                RightDoorShut = false;
                OOPTimer++;

                RunOutOfPower();
            }
            else
            {
                // Update lights
                if (LeftDoorLightCounter > 0) LeftDoorLightCounter--;
                if (RightDoorLightCounter > 0) RightDoorLightCounter--;

                LeftDoorLight = LeftDoorLightCounter > 0;
                RightDoorLight = RightDoorLightCounter > 0;
            }

            // Update the actual lights
            var sunblockers = room.updateList.Where(x => x is Sunblock).Cast<Sunblock>().ToList();
            Sunblock leftBlocker = sunblockers.FirstOrDefault(x => x.ID == 0);
            Sunblock rightBlocker = sunblockers.FirstOrDefault(x => x.ID == 1);
            if (leftBlocker != null) leftBlocker.visible = !LeftDoorLight;
            if (rightBlocker != null) rightBlocker.visible = !RightDoorLight;

            // Update timer thing
            if (InCams)
            {
                CamViewTimer++;
            }
            else
            {
                CamViewTimer = 0;
            }

            Hour = room.game.world.rainCycle.timer / (40 * 60); // 40 ticks per second, 60 seconds per hour (2 pips)

            // Update AIs
            for (int i = 0; i < AIs.Length; i++)
            {
                AIs[i].Update();
            }

            // Update jumpscare timer
            if (CurrentJumpscare != Animatronic.None)
            {
                if (jumpscareObj == null)
                {
                    jumpscareObj = new Jumpscare(room, CurrentJumpscare);
                    room.AddObject(jumpscareObj);
                }
                room.game.manager.musicPlayer?.FadeOutAllSongs(1f);

                foreach (var crit in room.game.Players)
                {
                    crit.realizedCreature?.Die();
                }

                JumpscareTimer++;
                if (JumpscareTimer > 30 && room.game.manager.upcomingProcess == null)
                {
                    LastJumpscare = CurrentJumpscare;
                    room.game.ExitGame(true, true);
                    room.game.GetStorySession.saveState.SessionEnded(room.game, false, false);
                    room.game.manager.RequestMainProcessSwitch(Constants.GameOverScreen, 0f);
                }
            }
        }

        public override void Destroy()
        {
            base.Destroy();
            Instance = null;
        }

        public void RunOutOfPower()
        {
            RemoveOverseer(ref lDoorOverseer, false);
            RemoveOverseer(ref rDoorOverseer, false);
            RemoveOverseer(ref camsOverseer, false);
        }

        private void CreateOverseer(OverseerTask task, out Overseer overseer)
        {
            var absCre = new AbstractCreature(room.world, StaticWorld.GetCreatureTemplate(CreatureTemplate.Type.Overseer), null, room.game.FirstAnyPlayer.pos, room.world.game.GetNewID())
            {
                saveCreature = false
            };
            room.abstractRoom.AddEntity(absCre);
            absCre.RealizeInRoom();
            overseer = absCre.realizedCreature as Overseer;

            CWTs.SetOverseerTask(overseer, task);
        }

        private void RemoveOverseer(ref Overseer os, bool attribute)
        {
            if (os == null) return;

            if (attribute)
            {
                os.SetKillTag(room.game.FirstAnyPlayer);
            }
            os.Die();
            os.abstractCreature.Destroy();
            os = null;
        }
        
        public void ToggleCams()
        {
            if (OutOfPower) return;

            InCams = !InCams;
            CamViewTimer = 0;
            if (InCams)
            {
                CreateOverseer(OverseerTask.Cameras, out camsOverseer);
            }
            else
            {
                RemoveOverseer(ref camsOverseer, true);
            }
        }

        public void SwitchCamViewing()
        {
            if (OutOfPower) return;
            CamViewing = CamSelected;
            CamViewTimer = 0;
        }

        public void SwitchCamSelecting(Map.Direction dir)
        {
            if (OutOfPower) return;
            var cons = Map.CameraConnections[CamSelected];
            Map.Location loc = dir switch
            {
                Map.Direction.Up => cons.Up,
                Map.Direction.Down => cons.Down,
                Map.Direction.Left => cons.Left,
                Map.Direction.Right => cons.Right,
                _ => throw new NotImplementedException()
            };

            if (loc != Map.Location.NOWHERE)
            {
                CamSelected = loc;
            }
        }

        public void ToggleDoor(Map.Direction side)
        {
            if (OutOfPower) return;
            if (side == Map.Direction.Left)
            {
                LeftDoorShut = !LeftDoorShut;
                if (LeftDoorShut)
                {
                    CreateOverseer(OverseerTask.LeftDoor, out lDoorOverseer);
                }
                else
                {
                    RemoveOverseer(ref lDoorOverseer, true);
                }
            }
            else if (side == Map.Direction.Right)
            {
                RightDoorShut = !RightDoorShut;
                if (RightDoorShut)
                {
                    CreateOverseer(OverseerTask.RightDoor, out rDoorOverseer);
                }
                else
                {
                    RemoveOverseer(ref rDoorOverseer, true);
                }
            }
        }

        public void FlickerCams()
        {
            CamViewTimer = 0;
        }
    }
}
