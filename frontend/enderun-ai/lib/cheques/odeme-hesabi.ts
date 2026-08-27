import { CashAccountType, type CashAccount } from "@/services/cash-account.service";
import { ChequeDirection, ChequeStatus } from "@/services/cheque.service";

/**
 * VERİLEN ÇEK KASADAN ÖDENMEZ (ÇEK/2).
 *
 * Banka çeki, üzerinde yazan bankadaki hesaptan tahsil edilir. Buna
 * rağmen canlıda iki çek Garanti yaprağı oldukları hâlde KASA
 * hesabından ödenmiş işaretlendi; açılır listede kasa ile banka yan
 * yana duruyordu.
 *
 * BURASI YETKİLİ YER DEĞİL — sunucu (`CekOdemeHesabiKurali`) yetkili.
 * Buradaki süzgeç, kullanıcıya seçemeyeceği bir şeyi hiç göstermemek
 * için. Sunucu tarafı kaldırılırsa bu süzgeç kuralı korumaz; o yüzden
 * kuralın sondası uçtadır, burada değil.
 */
export function odemeHesabiSecilebilirMi(
  direction: number,
  fromStatus: number,
  toStatus: number,
  account: Pick<CashAccount, "type">
): boolean {
  const verilenCekOdemesi =
    direction === ChequeDirection.Issued &&
    fromStatus === ChequeStatus.Issued &&
    toStatus === ChequeStatus.Paid;

  if (!verilenCekOdemesi) return true;

  return account.type !== CashAccountType.Cash;
}

/** Geçişe uygun hesaplar — listeyi doğrudan besler. */
export function secilebilirOdemeHesaplari<T extends Pick<CashAccount, "type">>(
  direction: number,
  fromStatus: number,
  toStatus: number,
  accounts: readonly T[]
): T[] {
  return accounts.filter((account) =>
    odemeHesabiSecilebilirMi(direction, fromStatus, toStatus, account)
  );
}
