using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Transport.Messages.Responses.Things.Actions;
using Transport.Messages.Responses.Things.Market;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class MarketLook
    {
        private static readonly AccessTools.FieldRef<StaticLocationLoadController, IList<SceneObjectInfo>> SceneObjects =
            AccessTools.FieldRefAccess<StaticLocationLoadController, IList<SceneObjectInfo>>("_sceneObjectInfos");
        private static readonly MethodInfo BuyMethod = AccessTools.Method(typeof(HintResolverContext), "OnMarketContentBuy");

        private static bool _here;
        private static int _asking;
        private static float _askedAt;
        private static ResponseCallbackContext _context;
        private static HintResolverContext _resolver;
        private static MarketProposalListDialog _dialog;
        private static bool _shown;

        internal static bool Here => _here;

        internal static bool Open => _shown;

        internal static void Seen(StaticLocationLoadController controller)
        {
            bool here = false;
            try
            {
                var infos = controller != null ? SceneObjects(controller) : null;
                if (infos != null)
                    foreach (var info in infos)
                        if (info != null && info.ObjectType == EObjectType.MarketBuy) { here = true; break; }
            }
            catch (Exception e) { Plugin.Caught("[market look] location objects: " + e.Message); }
            if (here != _here) Plugin.Trace("[market look] " + (here ? "on market" : "not on market"));
            _here = here;
        }

        internal static void Left()
        {
            _here = false;
        }

        internal static void Show(int thing, string name)
        {
            if (thing <= 0) return;
            if (!_here)
            {
                Notice.Show(Lang.T("Вы не на рынке. Предложения видны, только когда стоите на рынке"), 5f);
                return;
            }
            if (_shown || (_asking != 0 && Time.unscaledTime < _askedAt + 8f)) return;
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return;
                Release();
                _context = new ResponseCallbackContext();
                _asking = thing;
                _askedAt = Time.unscaledTime;
                var context = _context;
                nc.SendRequest(context, new OffersAsk(thing), ResponseConsts.PROPOSALS_RESPONCE, thing, msg =>
                {
                    try { Got(thing, name, context, msg as ProposalsResponceResponseMessage); }
                    catch (Exception e) { _asking = 0; Plugin.Fault("[market look] offers " + thing + ": " + e.Message); Release(); }
                });
                Plugin.Trace("[market look] asked offers for " + thing);
            }
            catch (Exception e) { _asking = 0; Plugin.Fault("[market look] ask " + thing + ": " + e.Message); Release(); }
        }

        private static void Got(int thing, string name, ResponseCallbackContext context, ProposalsResponceResponseMessage reply)
        {
            if (!ReferenceEquals(context, _context)) return;
            if (reply == null || reply.Items == null || reply.Items.Count == 0)
            {
                _asking = 0;
                Plugin.Trace("[market look] no offers for " + thing);
                Notice.Show(Lang.F("На рынке нет предложений: {0}", name), 4f);
                Release();
                return;
            }
            var about = DependencyContainer.GetContainer()?.Resolve<GeneralThingInfoDescriptionManager>();
            if (about == null) { _asking = 0; Release(); return; }
            about.Get(thing, description =>
            {
                try { Present(thing, description, context, reply); }
                catch (Exception e) { Plugin.Fault("[market look] window " + thing + ": " + e.Message); Release(); }
                finally { _asking = 0; }
            });
        }

        private static void Present(int thing, IGeneralThingInfoDescription description, ResponseCallbackContext context, ProposalsResponceResponseMessage reply)
        {
            if (!ReferenceEquals(context, _context) || description == null || !CraftCalc.IsOpen) { Release(); return; }
            _resolver = new HintResolverContext(EThingContextWindow.DEFAULT_WINDOW_BY_LOCATION);
            _resolver.OnThingHintActionSuccess += Bought;
            var resolver = _resolver;
            var dialog = DialogFactory.GetMarketProposalListDialog();
            var list = new List<MarketProposalListItemDTO>();
            var wrapper = new ListWrapper<MarketProposalListItemDTO>(list);
            foreach (var item in reply.Items)
            {
                if (item == null) continue;
                var dto = new MarketProposalListItemDTO(description, item);
                dto.OnBuy += () =>
                {
                    try
                    {
                        BuyMethod.Invoke(resolver, new object[]
                        {
                            description, wrapper, dto, EThingActionButton.BUY, EThingContextWindow.DEFAULT_WINDOW_BY_LOCATION,
                            null, dialog, context
                        });
                    }
                    catch (Exception e) { Plugin.Fault("[market look] buy: " + e.Message); }
                };
                list.Add(dto);
            }
            list.Sort();
            _dialog = dialog;
            _shown = true;
            dialog.OnDialogDestroy += Closed;
            CraftCalc.Under(true);
            dialog.ShowDialog(new MarketProposalListDialogParams(description, wrapper), ECanvasType.ModalWindow);
            Plugin.Trace("[market look] offers window for " + thing + ": " + list.Count);
        }

        private static void Bought(EThingActionButton button, ThingContextActionResponseMessage reply)
        {
            if (button == EThingActionButton.BUY) Notice.Show(Lang.T("Куплено"), 3f);
        }

        private static void Closed()
        {
            Plugin.Trace("[market look] offers window closed");
            Release();
        }

        internal static void Tick()
        {
            if (!_shown) return;
            if (_dialog == null || !_dialog.gameObject.activeInHierarchy) Release();
        }

        internal static void Drop()
        {
            if (_dialog != null)
            {
                try { _dialog.Close(); }
                catch (Exception e) { Plugin.Caught("[market look] closing offers: " + e.Message); }
            }
            Release();
        }

        private static void Release()
        {
            if (_dialog != null) _dialog.OnDialogDestroy -= Closed;
            _dialog = null;
            if (_shown) CraftCalc.Under(false);
            _shown = false;
            if (_resolver != null)
            {
                _resolver.OnThingHintActionSuccess -= Bought;
                _resolver = null;
            }
            if (_context != null)
            {
                try { _context.Destroy(); }
                catch (Exception e) { Plugin.Caught("[market look] reply context: " + e.Message); }
                _context = null;
            }
        }
    }

    internal sealed class OffersAsk : BaseRequest
    {
        private readonly Transport.Messages.Common.Single.SingleIntMessage _message;

        internal OffersAsk(int thing) : base(222)
        {
            _message = new Transport.Messages.Common.Single.SingleIntMessage(thing);
        }

        public override object GenerateMessage() => _message;
    }

    [HarmonyPatch(typeof(StaticLocationLoadController), "RegisterInteractionHandlers")]
    internal static class MarketLookSeenPatch
    {
        private static void Prefix(StaticLocationLoadController __instance) => MarketLook.Seen(__instance);
    }

    [HarmonyPatch(typeof(StaticLocationLoadController), "OnDestroy")]
    internal static class MarketLookLeftPatch
    {
        private static void Postfix() => MarketLook.Left();
    }
}
