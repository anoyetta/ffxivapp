﻿// FFXIVAPP.Client
// Timeline.cs
// 
// © 2013 Ryan Wilson

#region Usings

using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Timers;
using FFXIVAPP.Client.Delegates;
using FFXIVAPP.Client.Plugins.Parse.Enums;
using FFXIVAPP.Client.Plugins.Parse.Models.Fights;
using FFXIVAPP.Client.Plugins.Parse.Models.StatGroups;
using FFXIVAPP.Client.Plugins.Parse.Models.Stats;
using FFXIVAPP.Client.SettingsProviders.Parse;
using FFXIVAPP.Common.Utilities;
using NLog;
using SmartAssembly.Attributes;

#endregion

namespace FFXIVAPP.Client.Plugins.Parse.Models.Timelines
{
    [DoNotObfuscate]
    public sealed class Timeline : INotifyPropertyChanged
    {
        #region Property Bindings

        private bool _deathFound;
        private bool _fightingRightNow;

        public bool FightingRightNow
        {
            get { return _fightingRightNow; }
            private set
            {
                _fightingRightNow = value;
                RaisePropertyChanged();
            }
        }

        public bool DeathFound
        {
            get { return _deathFound; }
            private set
            {
                _deathFound = value;
                RaisePropertyChanged();
            }
        }

        public bool IsHistoryBased { get; set; }

        public FightList Fights { get; private set; }

        public StatGroup Overall { get; internal set; }

        public StatGroup Party { get; internal set; }

        public StatGroup Monster { get; internal set; }

        #endregion

        #region Events

        public event EventHandler<TimelineChangedEvent> TimelineChangedEvent = delegate { };

        private void RaiseTimelineChangedEvent(object sender, TimelineChangedEvent e)
        {
            TimelineChangedEvent(sender, e);
        }

        #endregion

        #region Declarations

        private readonly Timer _fightingTimer = new Timer(1500);
        private readonly Timer _storeHistoryTimer = new Timer(5000);

        public DateTime ParseStarted { get; set; }
        public uint ZoneID { get; set; }

        #endregion

        /// <summary>
        /// </summary>
        public Timeline(bool isHistoryBased = false)
        {
            IsHistoryBased = isHistoryBased;
            FightingRightNow = false;
            DeathFound = false;
            Fights = new FightList();
            Overall = new StatGroup("Overall");
            Party = new StatGroup("Party")
            {
                IncludeSelf = false
            };
            Monster = new StatGroup("Monster")
            {
                IncludeSelf = false
            };
            _fightingTimer.Elapsed += FightingTimerOnElapsed;
            ParseStarted = DateTime.Now;
            ZoneID = 0;
            try
            {
                double interval;
                double.TryParse(Settings.Default.StoreHistoryInterval, out interval);
                _storeHistoryTimer.Interval = interval;
            }
            catch (Exception ex)
            {
            }
            _storeHistoryTimer.Elapsed += StoreHistoryTimerOnElapsed;
            _storeHistoryTimer.Start();
        }

        private void FightingTimerOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            FightingRightNow = false;
            _fightingTimer.Stop();
            _storeHistoryTimer.Start();
        }

        private void StoreHistoryTimerOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            try
            {
                double interval;
                double.TryParse(Settings.Default.StoreHistoryInterval, out interval);
                _storeHistoryTimer.Interval = interval;
            }
            catch (Exception ex)
            {
            }
            if (!FightingRightNow && !IsHistoryBased)
            {
                ParseControl.Instance.StatMonitor.Clear();
            }
            _storeHistoryTimer.Stop();
        }

        /// <summary>
        /// </summary>
        /// <param name="mobName"> </param>
        /// <returns> </returns>
        public Monster GetSetMob(string mobName)
        {
            StatGroup statGroup;
            if (!Monster.TryGetGroup(mobName, out statGroup))
            {
                Logging.Log(LogManager.GetCurrentClassLogger(), String.Format("StatEvent : Adding new stat group for mob : {0}", mobName));
                statGroup = new Monster(mobName);
                Monster.AddGroup(statGroup);
            }
            return (Monster) statGroup;
        }

        /// <summary>
        /// </summary>
        /// <param name="playerName"> </param>
        /// <returns> </returns>
        public Player GetSetPlayer(string playerName)
        {
            StatGroup statGroup;
            if (Party.TryGetGroup(playerName, out statGroup))
            {
                return (Player) statGroup;
            }
            statGroup = new Player(playerName);
            Party.AddGroup(statGroup);
            return (Player) statGroup;
        }

        /// <summary>
        /// </summary>
        /// <param name="eventType"> </param>
        /// <param name="eventArgs"> </param>
        public void PublishTimelineEvent(TimelineEventType eventType, params object[] eventArgs)
        {
            var args = (eventArgs != null && eventArgs.Any()) ? eventArgs[0] : "(no args)";
            Logging.Log(LogManager.GetCurrentClassLogger(), String.Format("TimelineEvent : {0} {1}", eventType, args));
            if (eventArgs != null)
            {
                var mobName = eventArgs.First() as String;
                switch (eventType)
                {
                    case TimelineEventType.PartyJoin:
                        if ((string) eventArgs[0] == "You")
                        {
                            eventArgs[0] = Constants.CharacterName;
                        }
                        break;
                    case TimelineEventType.PartyDisband:
                        break;
                    case TimelineEventType.PartyLeave:
                        var whoLeft = eventArgs.Any() ? eventArgs.First() as string : String.Empty;
                        break;
                    case TimelineEventType.MobFighting:
                        FightingRightNow = true;
                        DeathFound = false;
                        _fightingTimer.Start();
                        if (mobName != null && (mobName.ToLower()
                                                       .Contains("target") || mobName == ""))
                        {
                            break;
                        }
                        Fight fighting;
                        if (!Fights.TryGet(mobName, out fighting))
                        {
                            fighting = new Fight(mobName);
                            Fights.Add(fighting);
                        }
                        ParseControl.Instance.LastEngaged = mobName;
                        break;
                    case TimelineEventType.MobKilled:
                        DeathFound = true;
                        if (mobName != null && (mobName.ToLower()
                                                       .Contains("target") || mobName == ""))
                        {
                            break;
                        }
                        Fight killed;
                        if (!Fights.TryGet(mobName, out killed))
                        {
                            killed = new Fight(mobName);
                            Fights.Add(killed);
                        }
                        GetSetMob(mobName)
                            .SetKill(killed);
                        ParseControl.Instance.LastKilled = mobName;
                        break;
                }
            }
            RaiseTimelineChangedEvent(this, new TimelineChangedEvent(eventType, eventArgs));
        }

        /// <summary>
        /// </summary>
        public static void Clear()
        {
            ParseControl.Instance.Timeline.Fights.Clear();
            ParseControl.Instance.Timeline.Overall.Clear();
            ParseControl.Instance.Timeline.Party.Clear();
            ParseControl.Instance.Timeline.Monster.Clear();
            ParseControl.Instance.Timeline.ParseStarted = DateTime.Now;
            if (MonsterWorkerDelegate.CurrentUser != null)
            {
                ParseControl.Instance.Timeline.ZoneID = MonsterWorkerDelegate.CurrentUser.MapIndex;
            }
        }

        #region Implementation of INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged = delegate { };

        private void RaisePropertyChanged([CallerMemberName] string caller = "")
        {
            PropertyChanged(this, new PropertyChangedEventArgs(caller));
        }

        #endregion
    }
}