using System;
using System.Collections.Generic;
using MopedSim.Core;
using MopedSim.UI;
using UnityEngine;
using UnityEngine.UI;

namespace MopedSim.App
{
    /// <summary>
    /// Wires the Parts Shop (F3) and Parts Alley Swap Meet views to the Core economy / garage. The scaffolded panels are empty
    /// stubs, so their interactive layouts are built at runtime on first open (same approach as the Settings menu). All rules
    /// (prices, cash, fit, install preconditions, swap meet defects) are Core calls; this class only dispatches and reports.
    /// </summary>
    internal sealed class ShopPanelsPresenter
    {
        private const int ShopSortingOrder = 250;

        private readonly AppCompositionRoot _app;
        private readonly PartsCatalogShopView _shop;
        private readonly SwapMeetView _swap;
        private readonly IReadOnlyList<PartDefinition> _parts;
        private bool _shopBuilt;
        private bool _swapBuilt;
        private string _feedback = string.Empty;

        internal ShopPanelsPresenter(AppCompositionRoot app, PartsCatalogShopView shop, SwapMeetView swap, IReadOnlyList<PartDefinition> parts)
        {
            _app = app;
            _shop = shop;
            _swap = swap;
            _parts = parts;
            if (_shop != null)
            {
                _shop.BuyPartRequested += OnBuy;
                _shop.InstallPartRequested += OnInstall;
                _shop.CloseRequested += CloseShop;
                _shop.SwapMeetRequested += OpenSwapMeet;
            }

            if (_swap != null)
            {
                _swap.BuyOfferRequested += OnSwapBuy;
                _swap.RotateRequested += OnSwapRotate;
                _swap.CloseRequested += CloseSwapMeet;
            }
        }

        internal void Dispose()
        {
            if (_shop != null)
            {
                _shop.BuyPartRequested -= OnBuy;
                _shop.InstallPartRequested -= OnInstall;
                _shop.CloseRequested -= CloseShop;
                _shop.SwapMeetRequested -= OpenSwapMeet;
            }

            if (_swap != null)
            {
                _swap.BuyOfferRequested -= OnSwapBuy;
                _swap.RotateRequested -= OnSwapRotate;
                _swap.CloseRequested -= CloseSwapMeet;
            }
        }

        /// <summary>Builds the layout (first time) and refreshes the list from Core. Call before activating the shop panel.</summary>
        internal void PrepareShop()
        {
            if (_shop == null)
            {
                return;
            }

            if (_swap != null) { _swap.gameObject.SetActive(false); }
            EnsureBuilt(_shop.transform as RectTransform, ref _shopBuilt, delegate { _shop.BuildDefaultLayout((RectTransform)_shop.transform); });
            _feedback = string.Empty;
            RenderShop();
        }

        internal void PrepareSwapMeet()
        {
            if (_swap == null)
            {
                return;
            }

            EnsureBuilt(_swap.transform as RectTransform, ref _swapBuilt, delegate { _swap.BuildDefaultLayout((RectTransform)_swap.transform); });
            _swap.SetMarket(_app.SwapMarket);
        }

        private void RenderShop()
        {
            _shop.InvalidateCache();
            _shop.RenderCatalog(_parts, _app.World, _app.ActiveBike, _feedback);
        }

        private void OnBuy(string partId)
        {
            PartDefinition part = Find(partId);
            SimulationWorld world = _app.World;
            if (part == null || world == null)
            {
                SetFeedback("That part is not available.");
                return;
            }

            PartInstance bought;
            PurchaseResult purchase = world.TryPurchasePartIntoInventory(part, out bought);
            SetFeedback(purchase.Succeeded
                ? purchase.Message + (IsToolOrSpare(part) ? " Pack it into the tool roll." : " It is in your parts crate.")
                : (purchase.Message.Length > 0 ? purchase.Message : "Not enough cash."));
        }

        private void OnInstall(string partId)
        {
            PartDefinition part = Find(partId);
            SimulationWorld world = _app.World;
            BikeSimulation bike = _app.ActiveBike;
            if (part == null || world == null)
            {
                SetFeedback("That part is not available.");
                return;
            }

            if (IsToolOrSpare(part))
            {
                if (world.Inventory.CountOf(partId) == 0)
                {
                    SetFeedback("Buy it first.");
                }
                else if (_app.TryPackToolRollItem(partId))
                {
                    SetFeedback(part.DisplayName + " packed into the tool roll.");
                }
                else
                {
                    SetFeedback("Tool roll is full.");
                }

                return;
            }

            PartInstance owned = world.Inventory.FindFirstByPartId(partId);
            if (owned == null || bike == null)
            {
                SetFeedback("Buy it first.");
                return;
            }

            string slotId = bike.Assembly.FindCompatibleSlotId(part);
            if (slotId == null)
            {
                SetFeedback(part.DisplayName + " does not fit this bike.");
                return;
            }

            PartInstance previous = bike.Assembly.GetInstalledPart(slotId);
            WrenchActionResult result = previous != null ? bike.TrySwapPart(slotId, owned) : bike.TryInstallPart(slotId, owned);
            if (!result.Succeeded)
            {
                SetFeedback(result.Message + " Finish the job at the workstand.");
                return;
            }

            world.Inventory.Remove(owned);
            if (previous != null)
            {
                world.Inventory.Add(previous);
            }

            if (_app.GarageContext != null)
            {
                _app.GarageContext.Publish(result);
            }

            SetFeedback(previous != null
                ? "Swapped in " + part.DisplayName + "; the old part went back to your crate."
                : "Installed " + part.DisplayName + ".");
        }

        private void OnSwapBuy(string offerId)
        {
            _app.TryBuySwapMeetOffer(offerId);
        }

        private void OnSwapRotate()
        {
            SwapMeetMarket market = _app.SwapMarket;
            if (market != null && _swap != null)
            {
                market.AdvanceRotation();
                _swap.RefreshOffers();
            }
        }

        private void SetFeedback(string text)
        {
            _feedback = text;
            RenderShop();
        }

        private void CloseShop()
        {
            if (_shop != null) { _shop.gameObject.SetActive(false); }
        }

        private void CloseSwapMeet()
        {
            if (_swap != null) { _swap.gameObject.SetActive(false); }
        }

        private void OpenSwapMeet()
        {
            if (_swap == null)
            {
                return;
            }

            CloseShop();
            PrepareSwapMeet();
            _swap.gameObject.SetActive(true);
        }

        private PartDefinition Find(string partId)
        {
            if (_parts == null || string.IsNullOrEmpty(partId))
            {
                return null;
            }

            for (int i = 0; i < _parts.Count; i++)
            {
                PartDefinition p = _parts[i];
                if (p != null && string.Equals(p.Id, partId, StringComparison.Ordinal))
                {
                    return p;
                }
            }

            return null;
        }

        private static bool IsToolOrSpare(PartDefinition part)
        {
            return part.Stats.IsTool || part.Stats.IsSpare || part.Category == PartCategory.ToolRollItem;
        }

        private delegate void BuildAction();

        /// <summary>
        /// Turns the scaffolded stub panel into a full-screen dimmed modal host above the HUD (hides its old stub children) and
        /// invokes <paramref name="build"/> once.
        /// </summary>
        private static void EnsureBuilt(RectTransform panel, ref bool built, BuildAction build)
        {
            if (built || panel == null)
            {
                return;
            }

            built = true;
            for (int i = 0; i < panel.childCount; i++)
            {
                panel.GetChild(i).gameObject.SetActive(false);
            }

            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;
            Image dim = panel.GetComponent<Image>();
            if (dim == null) { dim = panel.gameObject.AddComponent<Image>(); }
            dim.color = new Color(0f, 0f, 0f, 0.55f);

            Canvas c = panel.GetComponent<Canvas>();
            if (c == null)
            {
                c = panel.gameObject.AddComponent<Canvas>();
                panel.gameObject.AddComponent<GraphicRaycaster>();
            }

            c.overrideSorting = true;
            c.sortingOrder = ShopSortingOrder;
            build();
        }
    }
}
