using System.Collections.Generic;
using System.IO;
using MopedSim.App;
using MopedSim.Core;
using MopedSim.Data;
using MopedSim.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace MopedSim.Tests.EditMode
{
    /// <summary>W3-1: the F3 parts shop and the swap meet build real interactive layouts and run purchases / installs through Core.</summary>
    public sealed class ShopPanelsTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private const string ToolPartId = "SparkPlugWrench";
        private CatalogDatabase _db;
        private SimulationWorld _world;
        private BikeSimulation _bike;
        private AppCompositionRoot _app;
        private PartsCatalogShopView _shop;
        private SwapMeetView _swap;

        [SetUp]
        public void SetUp()
        {
            AppCompositionRoot.SkipTitleScreen = true;
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "data", "catalogs"));
            _db = CatalogLoader.LoadFromDirectory(dir);
            _bike = new BikeSimulation(_db.GetArchetype(ArchetypeIds.MaxerE50), _db.Parts);
            _world = new SimulationWorld(new SeededSimRandom(31u), _db.GetZone("Zone_WestOakland"));
            _world.CashUsd = 500m;
            _world.RegisterBike(AppCompositionRoot.DefaultBikeInstanceId, _bike);

            GameObject rootGo = Spawn("AppRoot_Shop");
            GameSessionStateMachine session = rootGo.AddComponent<GameSessionStateMachine>();
            _app = rootGo.AddComponent<AppCompositionRoot>();
            _app.BootstrapOnStart = false;
            _app.AssignCoreComponents(null, session, null, null);
            GameObject shopGo = Spawn("PartsShop", typeof(RectTransform));
            _shop = shopGo.AddComponent<PartsCatalogShopView>();
            shopGo.SetActive(false);
            GameObject swapGo = Spawn("SwapMeet", typeof(RectTransform));
            _swap = swapGo.AddComponent<SwapMeetView>();
            swapGo.SetActive(false);
            _app.AssignPresentationComponents(null, null, null, null, _shop, null, null);
            _app.AssignEconomyComponents(null, null, null, _swap, null, null);
            _app.BootstrapWithWorld(_world, _db.PartList, _db);
        }

        [TearDown]
        public void TearDown()
        {
            AppCompositionRoot.SkipTitleScreen = false;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null) { Object.DestroyImmediate(_spawned[i]); }
            }

            _spawned.Clear();
        }

        private GameObject Spawn(string name, params System.Type[] components)
        {
            GameObject go = new GameObject(name, components);
            _spawned.Add(go);
            return go;
        }

        private static Transform Find(Transform parent, string name)
        {
            if (parent.name == name) { return parent; }
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform t = Find(parent.GetChild(i), name);
                if (t != null) { return t; }
            }

            return null;
        }

        private Button RowButton(string partId, string button)
        {
            Transform row = Find(_shop.transform, "PartRow_" + partId);
            Assert.IsNotNull(row, "Row for " + partId + " missing (visible=" + _shop.VisibleCount + ").");
            Transform b = Find(row, button);
            Assert.IsNotNull(b);
            return b.GetComponent<Button>();
        }

        [Test]
        public void ToggleShop_BuildsTabsAndRowsFromCatalog_FitsFilterAndTabsWork()
        {
            Assert.IsTrue(_app.ToggleShop());
            Assert.IsNotNull(Find(_shop.transform, "Tab_Exhaust"));
            Assert.IsNotNull(Find(_shop.transform, "ShopScroll"));
            Assert.Greater(_shop.VisibleCount, 0);
            for (int i = 0; i < _shop.VisibleCount; i++)
            {
                PartDefinition p = _shop.GetVisible(i);
                bool tool = p.Stats.IsTool || p.Stats.IsSpare || p.Category == PartCategory.ToolRollItem
                    || p.Category == PartCategory.PremixOil || p.Category == PartCategory.Consumable;
                Assert.IsTrue(tool || _bike.Assembly.FindCompatibleSlotId(p) != null, p.Id + " is listed but does not fit the bike.");
                Assert.AreNotEqual(PartCategory.GarageStation, p.Category);
            }

            int fitting = _shop.VisibleCount;
            _shop.SetFitsBikeOnly(false);
            Assert.Greater(_shop.VisibleCount, fitting, "Showing everything must list parts that do not fit.");

            _shop.SelectTab(ShopTab.Exhaust);
            Assert.Greater(_shop.VisibleCount, 0);
            for (int i = 0; i < _shop.VisibleCount; i++)
            {
                Assert.AreEqual(PartCategory.Exhaust, _shop.GetVisible(i).Category);
            }
        }

        [Test]
        public void Buy_DeductsCash_AddsInventory_AndReportsFeedback()
        {
            _app.ToggleShop();
            PartDefinition crate = _db.GetPart("rack_milk_crate");
            decimal before = _world.CashUsd;
            RowButton(crate.Id, "Btn_Buy").onClick.Invoke();
            Assert.AreEqual(before - crate.PriceUsd, _world.CashUsd);
            Assert.AreEqual(1, _world.Inventory.CountOf(crate.Id));
            StringAssert.Contains("Bought", _shop.DisplayedFeedback);
            Assert.IsTrue(RowButton(crate.Id, "Btn_Install").interactable, "Owned part must be installable.");
        }

        [Test]
        public void Buy_WhenBroke_IsBlockedWithClearFeedback()
        {
            _world.CashUsd = 1m;
            _app.ToggleShop();
            PartDefinition crate = _db.GetPart("rack_milk_crate");
            Assert.IsFalse(RowButton(crate.Id, "Btn_Buy").interactable, "Unaffordable parts must have a disabled Buy button.");
            _shop.TriggerBuyPart(crate.Id);
            Assert.AreEqual(1m, _world.CashUsd);
            Assert.AreEqual(0, _world.Inventory.CountOf(crate.Id));
            StringAssert.Contains("Insufficient cash", _shop.DisplayedFeedback);
        }

        [Test]
        public void Install_PutsOwnedPartOnTheBike_AndSwapReturnsOldPartToCrate()
        {
            _app.ToggleShop();
            _shop.TriggerBuyPart("rack_milk_crate");
            string stockRackId = _bike.Assembly.GetInstalledPart(SlotIds.CargoRack).PartId;
            RowButton("rack_milk_crate", "Btn_Install").onClick.Invoke();
            PartInstance mounted = _bike.Assembly.GetInstalledPart(SlotIds.CargoRack);
            Assert.IsNotNull(mounted);
            Assert.AreEqual("rack_milk_crate", mounted.PartId);
            Assert.AreEqual(0, _world.Inventory.CountOf("rack_milk_crate"));
            Assert.AreEqual(1, _world.Inventory.CountOf(stockRackId), "The stock rack must go back to the crate.");

            _shop.TriggerBuyPart("rack_rear_wire");
            RowButton("rack_rear_wire", "Btn_Install").onClick.Invoke();
            Assert.AreEqual("rack_rear_wire", _bike.Assembly.GetInstalledPart(SlotIds.CargoRack).PartId);
            Assert.AreEqual(1, _world.Inventory.CountOf("rack_milk_crate"), "The swapped-out part must return to the crate.");
        }

        [Test]
        public void Tools_PackIntoToolRoll_ViaTheSameRow()
        {
            _app.ToggleShop();
            _shop.TriggerBuyPart(ToolPartId);
            Assert.AreEqual(1, _world.Inventory.CountOf(ToolPartId));
            RowButton(ToolPartId, "Btn_Install").onClick.Invoke();
            Assert.Contains(ToolPartId, new List<string>(_world.ActiveToolRoll));
        }

        [Test]
        public void EscapePrecedenceAndCloseButton_CloseTheShop()
        {
            Assert.IsTrue(_app.ToggleShop());
            Assert.IsTrue(_app.IsShopOpen);
            Assert.IsTrue(_app.CloseTransientPanels(), "Esc must close the shop before opening the pause menu.");
            Assert.IsFalse(_app.IsShopOpen);

            _app.ToggleShop();
            Find(_shop.transform, "Btn_Close").GetComponent<Button>().onClick.Invoke();
            Assert.IsFalse(_app.IsShopOpen);
        }

        [Test]
        public void SwapMeetButton_ClosesShop_AndBuildsOfferRows()
        {
            _app.ToggleShop();
            Find(_shop.transform, "Btn_SwapMeet").GetComponent<Button>().onClick.Invoke();
            Assert.IsFalse(_app.IsShopOpen);
            Assert.IsTrue(_swap.gameObject.activeSelf);
            Assert.Greater(_swap.OfferCount, 0);
            Transform offerRow = Find(_swap.transform, "OfferRow_" + _swap.GetOffer(0).OfferId);
            Assert.IsNotNull(offerRow);

            decimal before = _world.CashUsd;
            Find(offerRow, "Btn_Buy").GetComponent<Button>().onClick.Invoke();
            Assert.Less(_world.CashUsd, before, "Swap meet purchase must spend cash through Core.");
            Assert.IsTrue(_app.CloseTransientPanels());
            Assert.IsFalse(_swap.gameObject.activeSelf);
        }
    }
}
