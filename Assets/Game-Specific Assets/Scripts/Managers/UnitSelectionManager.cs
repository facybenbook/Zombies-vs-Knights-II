﻿using System;
using UnityEngine;
using UnityEngine.EventSystems;

public enum SelectionMode
{
    UnitSelect,
    UnitTarget
}

public class UnitSelectionManager : ManagerBase<UnitSelectionManager>
{
    #region Variables / Properties

    public string SelectAxis;
    public string ActionAxis;

    public Lockout SelectionLockout;
    public SelectionMode SelectionMode;
    public UnitActuator SelectedUnit;

    public string NeutralCursor;
    public string DefendCursor;
    public string AggressiveCursor;
    public string FriendlyCursor;

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

    private CursorRepository _cursorRepository;

    private Ray _ray;
    private RaycastHit _hit;

    #endregion Variables / Properties

    #region Hooks

    public void Start()
    {
        _player = PlayerManager.Instance;
        _cursorRepository = FindObjectOfType<CursorRepository>();

        SetCursor(NeutralCursor);
    }

    public void Update()
    {
        ProcessSelection();
        ProcessAction();
    }

    #endregion Hooks

    #region Methods

    public void SetCursor(string cursorState)
    {
        CursorModel model = _cursorRepository.GetCursorByName(cursorState);
        if (model == default(CursorModel))
        {
            DebugMessage("Could not find a cursor named " + cursorState + " in the cursor repository.");
            return;
        }

        if (model.Texture == null)
        {
            DebugMessage("No texture is associated to Cursor model " + model.Name, LogLevel.Error);
            return;
        }

        Cursor.SetCursor(model.Texture, model.Point, CursorMode.Auto);
    }

    public void UpdateCursor(GameObject gameObject)
    {
        string cursorModelName = NeutralCursor;

        switch (SelectionMode)
        {
            case SelectionMode.UnitSelect:
                // Do nothing; the neutral cursor is fine.
                break;

            case SelectionMode.UnitTarget:
                // Is the target game object even targetable?  If not, go no further.
                if (gameObject.tag != "Targetable")
                {
                    DebugMessage("Object " + gameObject.name + " is not tagged as targetable.");
                    break;
                }

                // If no unit is actually selected, revert to Unit Select mode, something is off.
                if (SelectedUnit == null)
                {
                    SelectionMode = SelectionMode.UnitSelect;
                    break;
                }

                // If an opposing unit is selected, the only valid option is a default cursor.
                // The player cannot control opposing units.
                bool isSelectedUnitOpposingPlayer = SelectedUnit.Faction != Player.Faction;
                if (isSelectedUnitOpposingPlayer)
                {
                    cursorModelName = NeutralCursor;
                    break;
                }

                // If the targeted object is a resource, and the selected unit is friendly, then
                // a MoveTo command will be issued.  Otherwise, keep the cursor default.
                ResourcePickupActuator resource = gameObject.GetComponent<ResourcePickupActuator>();
                if (resource != null
                    && SelectedUnit.Faction == Player.Faction)
                {
                    cursorModelName = FriendlyCursor;
                    break;
                }

                // If for some reason the unit is a null reference, go with a default cursor.
                // Log a warning, though...
                UnitActuator unit = gameObject.GetComponent<UnitActuator>();
                if(unit == null)
                {
                    cursorModelName = NeutralCursor;
                    DebugMessage("Attempted to change cursor for a null unit.", LogLevel.Warn);
                    break;
                }

                // If the target unit is the selected unit, the only option is for the
                // unit to defend its position.
                bool isSelf = SelectedUnit == unit;
                if (isSelf)
                {
                    cursorModelName = DefendCursor;
                    break;
                }

                // We know that A) the selected unit is on our side, and B)
                // the selected unit is not the target unit.  Therefore,
                // if the target unit isn't one of our...it's one of theirs.
                cursorModelName = unit.Faction != _player.Faction
                    ? AggressiveCursor
                    : FriendlyCursor;
                break;

            default:
                throw new InvalidOperationException("Unexpected selection mode: " + SelectionMode);
        }

        SetCursor(cursorModelName);
    }

    public void ClearSelection()
    {
        if (SelectedUnit == null)
            return;

        SelectedUnit.IssueCommand(AbilityCommmandTrigger.MatchOver);
        SelectedUnit = null;
    }

    private void ProcessSelection()
    {
        if (!Input.GetButton(SelectAxis) 
            || !SelectionLockout.CanAttempt())
            return;
        
        _ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(_ray, out _hit, 10000f))
        {
            GameObject target = _hit.collider.gameObject;

            switch (SelectionMode)
            {
                case SelectionMode.UnitSelect:
                case SelectionMode.UnitTarget:
                    if (IsMouseOverUIObject())
                    {
                        DebugMessage("Mouse is over a UI element; selecting nothing.");
                        break;
                    }

                    SelectTarget(target);

                    string cursor = NeutralCursor;
                    UnitActuator actuator = target.GetComponent<UnitActuator>();
                    if (actuator != null)
                    {
                        cursor = actuator.Faction == Player.Faction
                            ? DefendCursor
                            : NeutralCursor;
                    }

                    SetCursor(cursor);
                    break;

                default:
                    throw new InvalidOperationException("Unexpected selection mode: " + SelectionMode);
            }
        }

        SelectionLockout.NoteLastOccurrence();
    }

    private void ProcessAction()
    {
        if (!Input.GetButton(ActionAxis)
           || !SelectionLockout.CanAttempt())
            return;

        _ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(_ray, out _hit, 10000f))
        {
            GameObject target = _hit.collider.gameObject;

            switch (SelectionMode)
            {
                case SelectionMode.UnitSelect:
                    // Do nothing.
                    break;

                case SelectionMode.UnitTarget:
                    SelectUnitTarget(SelectedUnit, target);
                    break;

                default:
                    throw new InvalidOperationException("Unexpected selection mode: " + SelectionMode);
            }
        }

        SelectionLockout.NoteLastOccurrence();
    }

    public bool IsMouseOverUIObject()
    {
        bool result = EventSystem.current.currentSelectedGameObject != null;
        DebugMessage("Is the mouse over a UI object?  Answer: " + result);

        return result;
    }

    public void SelectTarget(GameObject target)
    {
        if (target.tag == "Terrain")
        {
            DeselectCurrent();
            return;
        }

        UnitActuator hitUnit = target.GetComponent<UnitActuator>();
        if (hitUnit != null)
        {
            DeselectCurrent();
            SelectedUnit = hitUnit;
            hitUnit.SelectUnit(_player.Faction);
            SelectionMode = SelectionMode.UnitTarget;
            return;
        }
    }

    public void SelectUnitTarget(UnitActuator unit, GameObject target)
    {
        if (target.tag == "Terrain")
        {
            DeselectCurrent();
            return;
        }

        if (target.tag != "Targetable")
        {
            FormattedDebugMessage(LogLevel.Warn,
                "Game Object {0} cannot be targeted with a MoveTo or Defend command.",
                target.name);
            return;
        }

        AbilityCommmandTrigger command = unit.gameObject == target
            ? AbilityCommmandTrigger.Defend
            : AbilityCommmandTrigger.MoveTo;

        unit.IssueCommand(command, target);
        
        return;
    }

    public void DeselectCurrent()
    {
        if (SelectedUnit == null)
            return;

        SelectedUnit.DeselectUnit();
        SelectedUnit = null;

        SelectionMode = SelectionMode.UnitSelect;
    }

    public void SelectTargetForUnit(UnitActuator unit)
    {

    }

    #endregion Methods
}
