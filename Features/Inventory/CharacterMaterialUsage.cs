using System;
using System.Collections.Generic;
using GameLogic;
using UI;

namespace AstralProbe.Features.Inventory {
    internal sealed class CharacterMaterialUsage {
        internal int HeroId;
        internal int SortOrder;
        internal string Name = "";
        internal string Portrait = "";
        internal int Quality;
        internal bool Owned;
        internal bool GiftPreferred;
        internal bool ContractMaterial;
        internal int ContractMissing;
        internal bool PotentialMaterial;
        internal bool PotentialAwakened;
        internal int PotentialMissing;
    }

    /// <summary>
    /// Reads the currently loaded AstralParty.Runtime objects directly. These
    /// are HybridCLR managed hotfix classes, not Il2CppInterop proxies.
    /// No cache of HeroCardData or static tables is kept between sessions.
    /// </summary>
    internal static class CharacterMaterialUsageReader {
        internal static bool TryRead(int itemId, out List<CharacterMaterialUsage> result) {
            result = new List<CharacterMaterialUsage>();
            if (itemId <= 0) return true;
            if (!GameLogicManager.hasInstance) return false;

            GameLogicManager manager = GameLogicManager.inst;
            HeroCardLogic heroCards = manager.heroCard;
            BagLogic bag = manager.bag;
            CharacterConfigure characters = StaticConfigure.Character;
            FavorConfigure favors = StaticConfigure.Favor;
            PVENurturanceConfigure potentials = StaticConfigure.PVENurturance;
            if (heroCards == null || bag == null || characters == null || favors == null || potentials == null) return false;

            int inBag = Math.Max(0, bag.GetItemCount(itemId));
            // Il2CppInterop-generated Protobuf collections are not guaranteed to
            // implement System.Collections.Generic.IEnumerable<T>. Use indexed
            // access for both native proxies and genuine HybridCLR collections.
            var infos = characters.Infos;
            int count = Math.Min(infos.Count, 512);
            for (int i = 0; i < count; i++) {
                CharacterInfoConfigure info = infos[i];
                if (info.HeroType != CharacterType.Hero || info.Id <= 0) continue;

                HeroCardData? card = heroCards.GetCardData(info.Id);
                SkinStandingPaintingConfigureItem? painting = card?.standingPainting;
                if (painting == null) painting = heroCards.GetConfigStandingPainting(info.Id, 0, 0);
                var usage = new CharacterMaterialUsage {
                    HeroId = info.Id,
                    SortOrder = info.OrderWeight,
                    Name = LocalName(info.NameID, info.Id),
                    Portrait = painting?.GetCharacterPhoto() ?? string.Empty,
                    Quality = painting == null ? 0 : (int)painting.SkinAppearanceType,
                    Owned = card?.IsHas ?? false
                };

                if (characters.HeroFavorGiftDict.TryGetValue(info.HeroFavorGift, out CharacterHeroFavorGiftConfigure giftConfig)) {
                    var gifts = giftConfig.CharacterHeroFavorGiftConfigureItems;
                    for (int j = 0; j < gifts.Count; j++) {
                        if (gifts[j].ItemID == itemId) {
                            usage.GiftPreferred = true;
                            break;
                        }
                    }
                }

                if (favors.BreakthroughDict.TryGetValue(info.FavorBreakthroughReward, out FavorBreakthroughConfigure contract)) {
                    int need = Need(contract.NeedMaterials, itemId);
                    usage.ContractMaterial = need > 0;
                    usage.ContractMissing = card?.isBreakThrough == true ? 0 : Math.Max(0, need - inBag);
                }

                var breaks = info.PveBreak;
                for (int j = 0; j < breaks.Count; j++) {
                    if (!potentials.BreakDict.TryGetValue(breaks[j], out PVENurturanceBreakConfigure talent)) continue;
                    int need = Need(talent.NeedMaterials, itemId);
                    if (need <= 0) continue;
                    usage.PotentialMaterial = true;
                    if (card?.PveData?.IsTalentUnlock(talent.Id) != true) usage.PotentialMissing += need;
                }
                PVENurturanceBreakConfigure? current = card?.PveData?.GetCurrentTalentConfigure();
                usage.PotentialAwakened = current != null && card?.PveData?.IsTalentUnlock(current.Id) == true;
                usage.PotentialMissing = usage.PotentialAwakened ? 0 : Math.Max(0, usage.PotentialMissing - inBag);

                if (usage.GiftPreferred || usage.ContractMaterial || usage.PotentialMaterial) result.Add(usage);
            }
            result.Sort((a, b) => a.SortOrder != b.SortOrder
                ? a.SortOrder.CompareTo(b.SortOrder) : a.HeroId.CompareTo(b.HeroId));
            return true;
        }

        private static int Need(Google.Protobuf.Collections.MapField<int, int> materials, int itemId) {
            return materials != null && materials.TryGetValue(itemId, out int count) ? count : 0;
        }

        private static string LocalName(int textId, int heroId) {
            if (textId <= 0 || StaticConfigure.STRCharacter?.LocalDict == null ||
                !StaticConfigure.STRCharacter.LocalDict.ContainsKey(textId)) return "#" + heroId;
            string localized = textId.GetLocal(UIStringType.Character);
            return string.IsNullOrWhiteSpace(localized) ? "#" + heroId : localized;
        }
    }
}
