﻿using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Acr.UserDialogs;
using Bit.App.Abstractions;
using Bit.App.Controls;
using Bit.App.Models.Page;
using Bit.App.Resources;
using Xamarin.Forms;
using XLabs.Ioc;
using Bit.App.Utilities;

namespace Bit.App.Pages
{
    public class VaultListSitesPage : ContentPage
    {
        private readonly IFolderService _folderService;
        private readonly ISiteService _siteService;
        private readonly IUserDialogs _userDialogs;
        private readonly IClipboardService _clipboardService;
        private readonly bool _favorites;

        public VaultListSitesPage(bool favorites)
        {
            _favorites = favorites;
            _folderService = Resolver.Resolve<IFolderService>();
            _siteService = Resolver.Resolve<ISiteService>();
            _userDialogs = Resolver.Resolve<IUserDialogs>();
            _clipboardService = Resolver.Resolve<IClipboardService>();

            Init();
        }

        public ExtendedObservableCollection<VaultListPageModel.Folder> Folders { get; private set; } = new ExtendedObservableCollection<VaultListPageModel.Folder>();

        private void Init()
        {
            if(!_favorites)
            {
                ToolbarItems.Add(new AddSiteToolBarItem(this));
            }

            var listView = new ListView
            {
                IsGroupingEnabled = true,
                ItemsSource = Folders,
                HasUnevenRows = true,
                GroupHeaderTemplate = new DataTemplate(() => new VaultListHeaderViewCell(this)),
                ItemTemplate = new DataTemplate(() => new VaultListViewCell(this))
            };
            if(Device.OS == TargetPlatform.iOS)
            {
                listView.Margin = new Thickness(0, 0, -15, 0);
            }
            listView.ItemSelected += SiteSelected;

            Title = _favorites ? AppResources.Favorites : AppResources.MyVault;
            Content = listView;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            LoadFoldersAsync().Wait();
        }

        private async Task LoadFoldersAsync()
        {
            var folders = await _folderService.GetAllAsync();
            var sites = _favorites ? await _siteService.GetAllAsync(true) : await _siteService.GetAllAsync();

            var pageFolders = folders.Select(f => new VaultListPageModel.Folder(f, sites.Where(s => s.FolderId == f.Id))).ToList();
            var noneFolder = new VaultListPageModel.Folder(sites.Where(s => s.FolderId == null));
            pageFolders.Add(noneFolder);

            Folders.ResetWithRange(pageFolders.Where(f => f.Any()));
        }

        private void SiteSelected(object sender, SelectedItemChangedEventArgs e)
        {
            var site = e.SelectedItem as VaultListPageModel.Site;
            var page = new ExtendedNavigationPage(new VaultViewSitePage(site.Id));
            Navigation.PushModalAsync(page);
        }

        private async void MoreClickedAsync(VaultListPageModel.Site site)
        {
            var selection = await DisplayActionSheet(site.Name, AppResources.Cancel, null,
                AppResources.View, AppResources.Edit, AppResources.CopyPassword, AppResources.CopyUsername, AppResources.GoToWebsite);

            if(selection == AppResources.View)
            {
                var page = new ExtendedNavigationPage(new VaultViewSitePage(site.Id));
                await Navigation.PushModalAsync(page);
            }
            else if(selection == AppResources.Edit)
            {
                var page = new ExtendedNavigationPage(new VaultEditSitePage(site.Id));
                await Navigation.PushModalAsync(page);
            }
            else if(selection == AppResources.CopyPassword)
            {
                Copy(site.Password, AppResources.Password);
            }
            else if(selection == AppResources.CopyUsername)
            {
                Copy(site.Username, AppResources.Username);
            }
            else if(selection == AppResources.GoToWebsite)
            {
                Device.OpenUri(new Uri(site.Uri));
            }
        }

        private void Copy(string copyText, string alertLabel)
        {
            _clipboardService.CopyToClipboard(copyText);
            _userDialogs.SuccessToast(string.Format(AppResources.ValueHasBeenCopied, alertLabel));
        }

        private async void DeleteClickedAsync(object sender, EventArgs e)
        {
            if(!await _userDialogs.ConfirmAsync(AppResources.DoYouReallyWantToDelete, null, AppResources.Yes, AppResources.No))
            {
                return;
            }

            var mi = sender as MenuItem;
            var site = mi.CommandParameter as VaultListPageModel.Site;
            var deleteCall = await _siteService.DeleteAsync(site.Id);

            if(deleteCall.Succeeded)
            {
                var folder = Folders.Single(f => f.Id == site.FolderId);
                var siteIndex = folder.Select((s, i) => new { s, i }).First(s => s.s.Id == site.Id).i;
                folder.RemoveAt(siteIndex);
                _userDialogs.SuccessToast(AppResources.SiteDeleted);
            }
            else if(deleteCall.Errors.Count() > 0)
            {
                await DisplayAlert(AppResources.AnErrorHasOccurred, deleteCall.Errors.First().Message, AppResources.Ok);
            }
        }

        private class AddSiteToolBarItem : ToolbarItem
        {
            private readonly VaultListSitesPage _page;

            public AddSiteToolBarItem(VaultListSitesPage page)
            {
                _page = page;
                Text = AppResources.Add;
                Icon = "plus";
                Clicked += ClickedItem;
            }

            private async void ClickedItem(object sender, EventArgs e)
            {
                var page = new ExtendedNavigationPage(new VaultAddSitePage());
                await _page.Navigation.PushModalAsync(page);
            }
        }

        private class VaultListViewCell : ExtendedTextCell
        {
            private VaultListSitesPage _page;

            public VaultListViewCell(VaultListSitesPage page)
            {
                _page = page;

                // Adding whitespace to Delete action to account for the negative margin offset on the listview
                var deleteAction = new MenuItem { Text = AppResources.Delete + "   ", IsDestructive = true };
                deleteAction.SetBinding(MenuItem.CommandParameterProperty, new Binding("."));
                deleteAction.Clicked += page.DeleteClickedAsync;

                var moreAction = new MenuItem { Text = AppResources.More };
                moreAction.SetBinding(MenuItem.CommandParameterProperty, new Binding("."));
                moreAction.Clicked += MoreAction_Clicked;

                SetBinding(CommandParameterProperty, new Binding("."));
                this.SetBinding<VaultListPageModel.Site>(TextProperty, s => s.Name);
                this.SetBinding<VaultListPageModel.Site>(DetailProperty, s => s.Username);

                ContextActions.Add(deleteAction);
                ContextActions.Add(moreAction);

                DisclousureTapped += VaultListViewCell_DisclousureTapped;
                ShowDisclousure = true;
                DisclousureImage = "more";
            }

            private void MoreAction_Clicked(object sender, EventArgs e)
            {
                var menuItem = sender as MenuItem;
                var site = menuItem.CommandParameter as VaultListPageModel.Site;
                _page.MoreClickedAsync(site);
            }

            private void VaultListViewCell_DisclousureTapped(object sender, EventArgs e)
            {
                var cell = sender as VaultListViewCell;
                var site = cell.CommandParameter as VaultListPageModel.Site;
                _page.MoreClickedAsync(site);
            }
        }

        private class VaultListHeaderViewCell : ViewCell
        {
            public VaultListHeaderViewCell(VaultListSitesPage page)
            {
                var image = new Image
                {
                    Source = "fa_folder_open.png",
                    Margin = new Thickness(16, 0, 0, 0),
                    VerticalOptions = LayoutOptions.CenterAndExpand
                };

                var label = new Label
                {
                    VerticalTextAlignment = TextAlignment.Center,
                    VerticalOptions = LayoutOptions.CenterAndExpand,
                    FontSize = 14,
                    Style = (Style)Application.Current.Resources["text-muted"]
                };

                label.SetBinding<VaultListPageModel.Folder>(Label.TextProperty, s => s.Name);

                var stackLayout = new StackLayout
                {
                    Orientation = StackOrientation.Horizontal,
                    VerticalOptions = LayoutOptions.FillAndExpand,
                    Children = { image, label },
                    BackgroundColor = Color.FromHex("ecf0f5")
                };

                View = stackLayout;
                Height = 30;
            }
        }
    }
}
