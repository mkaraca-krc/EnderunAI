import { AppShell } from "../../components/app-shell";
import { PageHeader } from "../../components/page-header";

const summaryCards = [
  { title: "Toplam Kasa", value: "₺ 4.850.000", note: "3 kasa hesabı" },
  { title: "Banka Bakiyesi", value: "₺ 18.420.000", note: "7 banka hesabı" },
  { title: "Bekleyen Tahsilat", value: "₺ 42.380.000", note: "12 açık kalem" },
  { title: "Bekleyen Ödeme", value: "₺ 15.270.000", note: "18 ödeme" },
];

const transactions = [
  { date: "16.07.2026", description: "KKB Hakediş Tahsilatı", account: "Garanti Bankası", type: "Gelir", amount: "₺ 3.250.000" },
  { date: "16.07.2026", description: "Personel Maaş Ödemesi", account: "Ziraat Bankası", type: "Gider", amount: "₺ 1.480.000" },
  { date: "15.07.2026", description: "MKE Malzeme Ödemesi", account: "İş Bankası", type: "Gider", amount: "₺ 685.000" },
  { date: "15.07.2026", description: "Deprem Okulları Tahsilatı", account: "Garanti Bankası", type: "Gelir", amount: "₺ 2.150.000" },
];

const payments = [
  { title: "Personel SGK", due: "18.07.2026", amount: "₺ 620.000" },
  { title: "Kablo Tedarikçisi", due: "19.07.2026", amount: "₺ 1.350.000" },
  { title: "Araç Kiralama", due: "20.07.2026", amount: "₺ 285.000" },
  { title: "Vergi Ödemesi", due: "22.07.2026", amount: "₺ 940.000" },
];

export default function FinancePage() {
  return (
    <AppShell active="Finans">
      <PageHeader
        title="Finans Merkezi"
        description="Kasa, banka, tahsilat, ödeme ve nakit akışı yönetimi"
        eyebrow="Enderun AI"
      />

      <section className="mt-8 grid gap-5 md:grid-cols-2 xl:grid-cols-4">
        {summaryCards.map((card) => (
          <article key={card.title} className="rounded-2xl border border-white/10 bg-white/[0.04] p-6">
            <p className="text-sm text-slate-400">{card.title}</p>
            <p className="mt-3 text-2xl font-bold text-cyan-300">{card.value}</p>
            <p className="mt-2 text-xs text-slate-500">{card.note}</p>
          </article>
        ))}
      </section>

      <section className="mt-8 grid gap-6 xl:grid-cols-[1.6fr_1fr]">
        <div className="rounded-2xl border border-white/10 bg-white/[0.04] p-6">
          <h2 className="text-xl font-bold">Son Finans Hareketleri</h2>
          <p className="mt-1 text-sm text-slate-400">Güncel banka ve kasa işlemleri</p>
          <div className="mt-6 overflow-x-auto">
            <table className="w-full min-w-[720px] text-left text-sm">
              <thead className="border-b border-white/10 text-slate-500">
                <tr>
                  <th className="pb-3 font-medium">Tarih</th>
                  <th className="pb-3 font-medium">Açıklama</th>
                  <th className="pb-3 font-medium">Hesap</th>
                  <th className="pb-3 font-medium">Tür</th>
                  <th className="pb-3 text-right font-medium">Tutar</th>
                </tr>
              </thead>
              <tbody>
                {transactions.map((item) => (
                  <tr key={`${item.date}-${item.description}`} className="border-b border-white/5">
                    <td className="py-4 text-slate-400">{item.date}</td>
                    <td className="py-4 font-medium">{item.description}</td>
                    <td className="py-4 text-slate-400">{item.account}</td>
                    <td className="py-4">
                      <span className={`rounded-full px-3 py-1 text-xs ${
                        item.type === "Gelir"
                          ? "bg-emerald-400/10 text-emerald-300"
                          : "bg-rose-400/10 text-rose-300"
                      }`}>
                        {item.type}
                      </span>
                    </td>
                    <td className="py-4 text-right font-semibold">{item.amount}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        <aside className="rounded-2xl border border-white/10 bg-white/[0.04] p-6">
          <h2 className="text-xl font-bold">Yaklaşan Ödemeler</h2>
          <p className="mt-1 text-sm text-slate-400">Önümüzdeki 7 gün</p>
          <div className="mt-6 space-y-4">
            {payments.map((payment) => (
              <div key={payment.title} className="rounded-xl border border-white/10 bg-slate-950/50 p-4">
                <div className="flex items-center justify-between gap-4">
                  <div>
                    <p className="font-medium">{payment.title}</p>
                    <p className="mt-1 text-xs text-slate-500">Vade: {payment.due}</p>
                  </div>
                  <p className="font-bold text-amber-300">{payment.amount}</p>
                </div>
              </div>
            ))}
          </div>
        </aside>
      </section>

      <section className="mt-8 rounded-2xl border border-cyan-400/20 bg-cyan-400/5 p-6">
        <p className="text-sm font-semibold text-cyan-300">AI Finans Yorumu</p>
        <p className="mt-3 max-w-4xl leading-7 text-slate-300">
          Mevcut örnek verilere göre önümüzdeki 7 günlük ödeme yükü yaklaşık
          ₺3,2 milyon. Bekleyen tahsilatların zamanında gerçekleşmesi halinde
          kısa vadeli nakit riski görünmüyor.
        </p>
      </section>
    </AppShell>
  );
}
