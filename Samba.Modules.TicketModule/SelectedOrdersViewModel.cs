﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using Microsoft.Practices.Prism.Commands;
using Samba.Domain.Models.Menus;
using Samba.Domain.Models.Tickets;
using Samba.Infrastructure;
using Samba.Localization.Properties;
using Samba.Persistance.Data;
using Samba.Presentation.Common;
using Samba.Presentation.Common.UIControls;
using Samba.Presentation.ViewModels;
using Samba.Services;
using Samba.Services.Common;

namespace Samba.Modules.TicketModule
{
    [Export]
    public class SelectedOrdersViewModel : ObservableObject
    {
        private bool _showExtraPropertyEditor;
        private bool _showTicketNoteEditor;
        private bool _showFreeTagEditor;
        private readonly IApplicationState _applicationState;
        private readonly ITicketService _ticketService;

        [ImportingConstructor]
        public SelectedOrdersViewModel(IApplicationState applicationState, ITicketService ticketService, 
            IUserService userService)
        {
            _applicationState = applicationState;
            _ticketService = ticketService;
            CloseCommand = new CaptionCommand<string>(Resources.Close, OnCloseCommandExecuted);
            SelectTicketTagCommand = new DelegateCommand<TicketTag>(OnTicketTagSelected);
            PortionSelectedCommand = new DelegateCommand<MenuItemPortion>(OnPortionSelected);
            OrderTagSelectedCommand = new DelegateCommand<OrderTagViewModel>(OnOrderTagSelected);
            UpdateExtraPropertiesCommand = new CaptionCommand<string>(Resources.Update, OnUpdateExtraProperties);
            UpdateFreeTagCommand = new CaptionCommand<string>(Resources.AddAndSave, OnUpdateFreeTag, CanUpdateFreeTag);
            SelectedItemPortions = new ObservableCollection<MenuItemPortion>();
            OrderTagGroups = new ObservableCollection<OrderTagGroupViewModel>();
            TicketTags = new ObservableCollection<TicketTag>();
            OrderTags = new ObservableCollection<OrderTagViewModel>();
            EventServiceFactory.EventService.GetEvent<GenericEvent<TicketViewModel>>().Subscribe(OnTicketViewModelEvent);
        }

        private void OnTicketViewModelEvent(EventParameters<TicketViewModel> obj)
        {
            if (obj.Topic == EventTopicNames.SelectOrderTag)
            {
                ResetValues(obj.Value);
                SelectedOrderTagGroup = obj.Value.LastSelectedOrderTag;
                OrderTags.AddRange(obj.Value.LastSelectedOrderTag.OrderTags.Select(x => new OrderTagViewModel(SelectedTicket.SelectedOrders.Select(u => u.Model), x)));
                if (OrderTags.Count == 1)
                {
                    SelectedTicket.FixSelectedItems();
                    SelectedTicket.SelectedOrders.ToList().ForEach(x =>
                        x.ToggleOrderTag(SelectedOrderTagGroup, OrderTags[0].Model, _applicationState.CurrentLoggedInUser.Id));
                    if (SelectedOrderTagGroup.IsSingleSelection)
                        obj.Value.ClearSelectedItems();
                }
                RaisePropertyChanged(() => OrderTagColumnCount);
            }

            if (obj.Topic == EventTopicNames.SelectTicketTag)
            {
                ResetValues(obj.Value);
                _showFreeTagEditor = SelectedTicket.LastSelectedTicketTag.FreeTagging;

                List<TicketTag> ticketTags;
                if (_showFreeTagEditor)
                {
                    ticketTags = Dao.Query<TicketTagGroup>(x => x.Id == SelectedTicket.LastSelectedTicketTag.Id,
                                                 x => x.TicketTags).SelectMany(x => x.TicketTags).OrderBy(x => x.Name).ToList();
                }
                else
                {
                    ticketTags = _applicationState.CurrentDepartment.TicketTemplate.TicketTagGroups.Where(
                           x => x.Name == obj.Value.LastSelectedTicketTag.Name).SelectMany(x => x.TicketTags).ToList();
                }
                ticketTags.Sort(new AlphanumComparator());
                TicketTags.AddRange(ticketTags);

                if (SelectedTicket.IsTaggedWith(SelectedTicket.LastSelectedTicketTag.Name)) TicketTags.Add(TicketTag.Empty);
                if (TicketTags.Count == 1 && !_showFreeTagEditor)
                {
                    _ticketService.UpdateTag(obj.Value.Model, SelectedTicket.LastSelectedTicketTag, TicketTags[0]);
                    SelectedTicket.ClearSelectedItems();
                }

                RaisePropertyChanged(() => TagColumnCount);
                RaisePropertyChanged(() => IsFreeTagEditorVisible);
                RaisePropertyChanged(() => FilteredTextBoxType);
            }

            if (obj.Topic == EventTopicNames.SelectExtraProperty)
            {
                ResetValues(obj.Value);
                _showExtraPropertyEditor = true;
                RaisePropertyChanged(() => IsExtraPropertyEditorVisible);
                RaisePropertyChanged(() => IsPortionsVisible);
            }

            if (obj.Topic == EventTopicNames.EditTicketNote)
            {
                ResetValues(obj.Value);
                _showTicketNoteEditor = true;
                RaisePropertyChanged(() => IsTicketNoteEditorVisible);
            }
        }

        private void ResetValues(TicketViewModel selectedTicket)
        {
            SelectedTicket = null;
            SelectedItem = null;
            SelectedOrderTagGroup = null;
            SelectedItemPortions.Clear();
            OrderTagGroups.Clear();
            TicketTags.Clear();
            OrderTags.Clear();
            _showExtraPropertyEditor = false;
            _showTicketNoteEditor = false;
            _showFreeTagEditor = false;
            SetSelectedTicket(selectedTicket);
        }

        public TicketViewModel SelectedTicket { get; private set; }
        public OrderViewModel SelectedItem { get; private set; }
        public OrderTagGroup SelectedOrderTagGroup { get; set; }

        public ICaptionCommand CloseCommand { get; set; }
        public ICaptionCommand UpdateExtraPropertiesCommand { get; set; }
        public ICaptionCommand UpdateFreeTagCommand { get; set; }
        public ICommand SelectTicketTagCommand { get; set; }

        public DelegateCommand<MenuItemPortion> PortionSelectedCommand { get; set; }
        public ObservableCollection<MenuItemPortion> SelectedItemPortions { get; set; }

        public DelegateCommand<OrderTagViewModel> OrderTagSelectedCommand { get; set; }
        public ObservableCollection<OrderTagGroupViewModel> OrderTagGroups { get; set; }

        public ObservableCollection<TicketTag> TicketTags { get; set; }
        public ObservableCollection<OrderTagViewModel> OrderTags { get; set; }

        public int TagColumnCount { get { return TicketTags.Count % 7 == 0 ? TicketTags.Count / 7 : (TicketTags.Count / 7) + 1; } }
        public int OrderTagColumnCount { get { return OrderTags.Count % 7 == 0 ? OrderTags.Count / 7 : (OrderTags.Count / 7) + 1; } }

        public FilteredTextBox.FilteredTextBoxType FilteredTextBoxType
        {
            get
            {
                if (SelectedTicket != null && SelectedTicket.LastSelectedTicketTag != null)
                {
                    if (SelectedTicket.LastSelectedTicketTag.IsInteger)
                        return FilteredTextBox.FilteredTextBoxType.Digits;
                    if (SelectedTicket.LastSelectedTicketTag.IsDecimal)
                        return FilteredTextBox.FilteredTextBoxType.Number;
                }
                return FilteredTextBox.FilteredTextBoxType.Letters;
            }
        }

        private string _freeTag;

        public string FreeTag
        {
            get { return _freeTag; }
            set
            {
                _freeTag = value;
                RaisePropertyChanged(() => FreeTag);
            }
        }

        public bool IsPortionsVisible
        {
            get
            {
                return SelectedItem != null
                    && SelectedItem.Model.DecreaseInventory
                    && !SelectedItem.IsLocked
                    && SelectedItemPortions.Count > 0;
            }
        }

        private void OnCloseCommandExecuted(string obj)
        {
            _showTicketNoteEditor = false;
            _showExtraPropertyEditor = false;
            _showFreeTagEditor = false;
            FreeTag = string.Empty;
            SelectedTicket.ClearSelectedItems();
        }

        public bool IsFreeTagEditorVisible { get { return _showFreeTagEditor; } }
        public bool IsExtraPropertyEditorVisible { get { return _showExtraPropertyEditor && SelectedItem != null; } }
        public bool IsTicketNoteEditorVisible { get { return _showTicketNoteEditor; } }


        private bool CanUpdateFreeTag(string arg)
        {
            return !string.IsNullOrEmpty(FreeTag);
        }

        private void OnUpdateFreeTag(string obj)
        {
            var cachedTag = _applicationState.CurrentDepartment.TicketTemplate.TicketTagGroups.Single(
                x => x.Id == SelectedTicket.LastSelectedTicketTag.Id);
            Debug.Assert(cachedTag != null);
            var ctag = cachedTag.TicketTags.SingleOrDefault(x => x.Name.ToLower() == FreeTag.ToLower());
            if (ctag == null && cachedTag.SaveFreeTags)
            {
                using (var workspace = WorkspaceFactory.Create())
                {
                    var tt = workspace.Single<TicketTagGroup>(x => x.Id == SelectedTicket.LastSelectedTicketTag.Id);
                    Debug.Assert(tt != null);
                    var tag = tt.TicketTags.SingleOrDefault(x => x.Name.ToLower() == FreeTag.ToLower());
                    if (tag == null)
                    {
                        tag = new TicketTag { Name = FreeTag };
                        tt.TicketTags.Add(tag);
                        workspace.Add(tag);
                        workspace.CommitChanges();
                    }
                }
            }
            _ticketService.UpdateTag(SelectedTicket.Model, SelectedTicket.LastSelectedTicketTag, new TicketTag { Name = FreeTag });
            SelectedTicket.ClearSelectedItems();
            FreeTag = string.Empty;
        }

        private void OnUpdateExtraProperties(string obj)
        {
            SelectedTicket.RefreshVisuals();
            _showExtraPropertyEditor = false;
            RaisePropertyChanged(() => IsExtraPropertyEditorVisible);
        }

        private void OnTicketTagSelected(TicketTag obj)
        {
            _ticketService.UpdateTag(SelectedTicket.Model, SelectedTicket.LastSelectedTicketTag, obj);
            SelectedTicket.ClearSelectedItems();
        }

        private void OnPortionSelected(MenuItemPortion obj)
        {
            SelectedItem.UpdatePortion(obj, _applicationState.CurrentDepartment.TicketTemplate.PriceTag);
            if (OrderTagGroups.Count == 0)
                SelectedTicket.ClearSelectedItems();
        }

        private void OnOrderTagSelected(OrderTagViewModel orderTag)
        {
            var mig = SelectedOrderTagGroup ?? OrderTagGroups.FirstOrDefault(propertyGroup => propertyGroup.OrderTags.Contains(orderTag)).Model;
            Debug.Assert(mig != null);
            SelectedTicket.FixSelectedItems();
            SelectedTicket.SelectedOrders.ToList().ForEach(x =>
                x.ToggleOrderTag(mig, orderTag.Model, _applicationState.CurrentLoggedInUser.Id));
            OrderTagGroups.ToList().ForEach(x => x.OrderTags.ToList().ForEach(u => u.Refresh()));
            if (SelectedOrderTagGroup != null) SelectedTicket.ClearSelectedItems();
            SelectedTicket.RefreshVisuals();
        }

        private void SetSelectedTicket(TicketViewModel ticketViewModel)
        {
            SelectedTicket = ticketViewModel;
            SelectedItem = SelectedTicket.SelectedOrders.Count() == 1 ? SelectedTicket.SelectedOrders[0] : null;
            RaisePropertyChanged(() => SelectedTicket);
            RaisePropertyChanged(() => SelectedItem);
            RaisePropertyChanged(() => IsTicketNoteEditorVisible);
            RaisePropertyChanged(() => IsExtraPropertyEditorVisible);
            RaisePropertyChanged(() => IsFreeTagEditorVisible);
            RaisePropertyChanged(() => IsPortionsVisible);
        }

        public bool ShouldDisplay(TicketViewModel value)
        {
            ResetValues(value);

            if (SelectedItem == null || SelectedItem.Model.Locked) return false;

            if (SelectedTicket != null && SelectedItem.Model.DecreaseInventory && !SelectedItem.Model.Locked)
            {
                if (SelectedItem.Model.PortionCount > 1) SelectedItemPortions.AddRange(SelectedItem.MenuItem.Portions);
                OrderTagGroups.AddRange(
                    _ticketService.GetOrderTagGroupsForItem(SelectedItem.MenuItem)
                    .Where(x => string.IsNullOrEmpty(x.ButtonHeader))
                    .Select(x => new OrderTagGroupViewModel(SelectedTicket.SelectedOrders.Select(y => y.Model)){Model = x}));
                RaisePropertyChanged(() => IsPortionsVisible);
            }

            return SelectedItemPortions.Count > 1 || OrderTagGroups.Count > 0;
        }

    }
}
