﻿using System;
using System.Linq;
using System.Collections.Generic;

public enum ActivityType
{
    Suspend,
    Resume
}

public class MatchController : ManagerBase<MatchController>, ISuspendable
{
    #region Substructures

    [Serializable]
    private struct KeyUnitHPState
    {
        public int HP;
        public int MaxHP;
    }

    #endregion Substructure

    #region Variables / Properties

    public bool AutoGenerateResources = true;
    public ResourceStateModel KnightResources;
    public ResourceStateModel ZombieResources;

    public List<UnitActuator> AllUnits
    {
        get
        {
            return FindObjectsOfType<UnitActuator>().ToList();
        }
    }

    public List<UnitActuator> AllAIUnits
    {
        get
        {
            List<UnitActuator> allAIUnits = new List<UnitActuator>();
            for (int i = 0; i < AllUnits.Count; i++)
            {
                UnitActuator current = AllUnits[i];
                if (current.Faction == Player.Faction)
                    continue;

                allAIUnits.Add(current);
            }

            return allAIUnits;
        }
    }

    public List<UnitActuator> AllPlayerUnits
    {
        get
        {
            List<UnitActuator> allPlayerUnits = new List<UnitActuator>();
            for (int i = 0; i < AllUnits.Count; i++)
            {
                UnitActuator current = AllUnits[i];
                if (current.Faction != Player.Faction)
                    continue;

                allPlayerUnits.Add(current);
            }

            return allPlayerUnits;
        }
    }

    private PlayerManager _player;
    private PlayerManager Player
    {
        get
        {
            if (_player == null)
                _player = PlayerManager.Instance;

            return _player;
        }
    }

    private MapController _map;
    private MapController Map
    {
        get
        {
            if (_map == null)
                _map = MapController.Instance;

            return _map;
        }
    }

    private GameEventController _gameEvent;
    private GameEventController GameEvent
    {
        get
        {
            if (_gameEvent == null)
                _gameEvent = GameEventController.Instance;

            return _gameEvent;
        }
    }

    private GameUIMasterController _gameUI;
    private GameUIMasterController GameUI
    {
        get
        {
            if (_gameUI == null)
                _gameUI = GameUIMasterController.Instance;

            return _gameUI;
        }
    }

    #endregion Variables / Properties

    #region Hooks

    public void Start()
    {
        // Initialize Faction Resources and the Faction Resource UI immediately.
        KnightResources = new ResourceStateModel(Faction.Knights);
        ZombieResources = new ResourceStateModel(Faction.Zombies);

        ResourceStateModel resources = GetPlayerResourceState();
        GameUI.UpdateResourceCount(resources.Count, resources.Cap);
    }

    public void Update()
    {
        CheckAutoResourceGeneration(KnightResources);
        CheckAutoResourceGeneration(ZombieResources);
    }

    #endregion Hooks

    #region Methods

    public void Suspend()
    {
        AutoGenerateResources = false;
    }

    public void Resume()
    {
        AutoGenerateResources = true;
    }

    public void SpawnUnitByUnitName(string unitName, Faction faction = Faction.None)
    {
        Ability ability = Map.GetUnitSpawnAbilityByUnitName(unitName);
        if(ability == null)
        {
            FormattedDebugMessage(LogLevel.Error, "No unit spawn ability exists for unit {0}", unitName);
            return;
        }

        UseUnitSpawnAbility(ability, faction);
    }

    public void UseUnitSpawnAbility(Ability ability, Faction faction = Faction.None)
    {
        if (faction == Faction.None)
            faction = Player.Faction;

        if (! IsPurchaseSuccessful(faction, ability))
        {
            if(faction == Player.Faction)
                GameUI.PresentTooltip("Cannot afford unit...");

            return;
        }

        FormattedDebugMessage(LogLevel.Info,
            "Presenter - using Create Unit Ability {0} for faction {1}",
            ability.Name,
            faction);

        GameEvent.RunGameEventGroup(ability.GameEvents);
    }

    public void AcquireKeyUnitHPCount()
    {
        var keyUnits = GetKeyUnits();
        KeyUnitHPState hpState = GetKeyUnitHPForFaction(Player.Faction, keyUnits);
        GameUI.UpdateKeyStructureHP(hpState.HP, hpState.MaxHP);
    }

    public void CheckForMatchConclusion()
    {
        var keyUnits = GetKeyUnits();

        bool playerWins = GetKeyUnitsForFaction(Player.Faction, keyUnits).Count > 0;
        bool playerLost = GetKeyUnitsForFaction(Player.Faction, keyUnits).Count == 0;

        // Determine match state...
        MatchState state = MatchState.OnGoing;
        if (playerWins)
            state = MatchState.Victory;
        else if (playerLost)
            state = MatchState.Lost;

        RadiateActivityCommand(ActivityType.Suspend);
        Player.RecordMatchOutcome(state);
        GameUI.ShowMatchOutcome(state);

        KeyUnitHPState hpState = GetKeyUnitHPForFaction(Player.Faction, keyUnits);
        GameUI.UpdateKeyStructureHP(hpState.HP, hpState.MaxHP);
    }

    private List<UnitActuator> GetKeyUnits()
    {
        var result = new List<UnitActuator>();
        List<UnitActuator> allUnits = AllUnits;
        for (int i = 0; i < allUnits.Count; i++)
        {
            UnitActuator current = allUnits[i];
            if (!current.IsKeyUnit)
                continue;

            if (current.IsDead)
                continue;

            result.Add(current);
        }

        return result;
    }

    private List<UnitActuator> GetKeyUnitsForFaction(Faction faction, List<UnitActuator> sourceList)
    {
        var result = new List<UnitActuator>();

        for (int i = 0; i < sourceList.Count; i++)
        {
            UnitActuator current = sourceList[i];
            if (current.Faction != faction)
                continue;

            if (current.IsDead)
                continue;

            result.Add(current);
        }

        return result;
    }

    private KeyUnitHPState GetKeyUnitHPForFaction(Faction faction, List<UnitActuator> keyUnits)
    {
        int hpResult = 0;
        int maxHpResult = 0;
        
        for(int i = 0; i < keyUnits.Count; i++)
        {
            UnitActuator current = keyUnits[i];
            if (current.Faction != faction)
                continue;

            hpResult += current.HP;
            maxHpResult += current.MaxHP;
        }

        return new KeyUnitHPState
        {
            HP = hpResult,
            MaxHP = maxHpResult
        };
    }

    public UnitActuator GetFirstOpposingKeyUnit(Faction faction)
    {
        UnitActuator result = null;

        var allKeyUnits = GetKeyUnits();

        Faction opposingFaction;
        switch (faction)
        {
            case Faction.Knights:
                opposingFaction = Faction.Zombies;
                break;

            case Faction.Zombies:
                opposingFaction = Faction.Knights;
                break;

            default:
                throw new InvalidOperationException("Unexpected faction: " + faction);
        }

        List<UnitActuator> opposingUnits = GetKeyUnitsForFaction(opposingFaction, allKeyUnits);
        if (!opposingUnits.IsNullOrEmpty())
            result = opposingUnits[0];

        return result;
    }

    public bool IsPurchaseSuccessful(Faction faction, Ability ability)
    {
        ResourceStateModel resource = GetResourceState(faction);
        bool result = resource.CanAfford(ability.ResourceCost);
        if(result)
        {
            resource.Spend(ability.ResourceCost);
            UpdateResourcesOnGUI(resource);
        }

        return result;
    }

    private ResourceStateModel GetPlayerResourceState()
    {
        return GetResourceState(Player.Faction);
    }

    private ResourceStateModel GetResourceState(Faction faction)
    {
        switch (faction)
        {
            case Faction.Knights:
                return KnightResources;

            case Faction.Zombies:
                return ZombieResources;

            default:
                throw new InvalidOperationException("Unexpected faction: " + Player.Faction);
        }
    }

    public void AwardResourcesToFaction(Faction faction, int award)
    {
        ResourceStateModel resources = GetResourceState(faction);

        resources.Gain(award);
        UpdateResourcesOnGUI(resources);
    }

    private void UpdateResourcesOnGUI(ResourceStateModel resources)
    {
        if (resources.Faction != Player.Faction)
            return;

        GameUI.UpdateResourceCount(resources.Count, resources.Cap);
    }

    private void CheckAutoResourceGeneration(ResourceStateModel factionResource)
    {
        if (!factionResource.AutoGenerateLockout.CanAttempt())
            return;

        factionResource.Gain(factionResource.AutoGenerateAmount);
        factionResource.AutoGenerateLockout.NoteLastOccurrence();

        UpdateResourcesOnGUI(factionResource);
    }

    private void RadiateActivityCommand(ActivityType activity)
    {
        var pausableGameObjects = FindObjectsOfType<ObjectPauser>();
        if (pausableGameObjects.Length == 0)
            throw new InvalidOperationException("There are no Pausable Objects in the scene!");

        switch (activity)
        {
            case ActivityType.Suspend:
                for (int i = 0; i < pausableGameObjects.Length; i++)
                {
                    ObjectPauser current = pausableGameObjects[i];
                    current.Suspend();
                }
                break;

            case ActivityType.Resume:
                for (int i = 0; i < pausableGameObjects.Length; i++)
                {
                    ObjectPauser current = pausableGameObjects[i];
                    current.Resume();
                }
                break;
        }
    }

    #endregion Methods
}


    