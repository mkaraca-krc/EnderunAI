import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { DataTable, type DataTableColumn } from "@/components/ui/data-table";

/**
 * STANDART LİSTE TABLOSU.
 *
 * Denetimde 143 liste ekranının HİÇBİRİNDE sayfalama yoktu. Bu
 * bileşen o boşluğu tek yerden kapatıyor, dolayısıyla buradaki her
 * hata 143 ekrana birden yayılır.
 *
 * En kritik iki söz:
 * - "Toplam X kayıt" GERÇEK toplamı söyler (poz ekranındaki kusur tam
 *   olarak buydu: kırpılmış liste toplam diye gösterilmişti).
 * - Filtre değişince sayfa 1'e döner; yoksa kullanıcı 7. sayfadayken
 *   filtreyi daraltınca boş ekranda kalır.
 */


/**
 * İNDİRME TAKLİDİ.
 *
 * jsdom `URL.createObjectURL` / `revokeObjectURL` uygulamıyor ve
 * `<a download>` tıklaması "navigation to another Document" hatası
 * veriyor. Üçünü birden kapatmayan bir taklit, testleri geçerken
 * arka planda yakalanmamış hata bırakıyordu — o da gerçek hatayı
 * maskeler.
 *
 * Geriye üretilen CSV metnini döndürür.
 */
function captureDownload(run: () => void): { content: string; name: string } {
  const originalBlob = globalThis.Blob;
  const originalCreate = URL.createObjectURL;
  const originalRevoke = URL.revokeObjectURL;
  const originalClick = HTMLAnchorElement.prototype.click;

  let content = "";
  let name = "";

  class SpyBlob extends originalBlob {
    constructor(parts: BlobPart[], options?: BlobPropertyBag) {
      super(parts, options);
      content = String(parts[0]);
    }
  }

  globalThis.Blob = SpyBlob as unknown as typeof Blob;
  URL.createObjectURL = vi.fn(() => "blob:test");
  URL.revokeObjectURL = vi.fn();
  HTMLAnchorElement.prototype.click = function click(this: HTMLAnchorElement) {
    name = this.download;
  };

  try {
    run();
    return { content, name };
  } finally {
    globalThis.Blob = originalBlob;
    URL.createObjectURL = originalCreate;
    URL.revokeObjectURL = originalRevoke;
    HTMLAnchorElement.prototype.click = originalClick;
  }
}

type Row = { id: string; ad: string; tutar: number };

const columns: DataTableColumn<Row>[] = [
  { key: "ad", header: "Ad", value: (row) => row.ad },
  { key: "tutar", header: "Tutar", value: (row) => row.tutar, numeric: true },
];

function rows(count: number): Row[] {
  return Array.from({ length: count }, (_, i) => ({
    id: String(i),
    ad: `Kayıt ${i}`,
    tutar: i * 10,
  }));
}

function bodyRows() {
  const table = screen.getByRole("table");
  const body = table.querySelectorAll<HTMLTableRowElement>("tbody tr");
  return Array.from(body);
}

describe("DataTable — istemci kipi", () => {
  it("varsayılan sayfa boyutu kadar satır gösterir", () => {
    render(
      <DataTable rows={rows(60)} columns={columns} rowKey={(r) => r.id} />
    );

    expect(bodyRows()).toHaveLength(25);
    expect(screen.getByText(/Sayfa 1 \/ 3/)).toBeInTheDocument();
  });

  it("toplam GERÇEK kayıt sayısını söyler, gösterileni değil", () => {
    render(
      <DataTable rows={rows(60)} columns={columns} rowKey={(r) => r.id} />
    );

    // Asıl kusurun imzası: 25 yazıp toplam demek.
    expect(screen.getByText(/Toplam 60 kayıt/)).toBeInTheDocument();
    expect(screen.getByText(/1–25 arası/)).toBeInTheDocument();
  });

  it("sonraki sayfaya geçer ve aralığı günceller", () => {
    render(
      <DataTable rows={rows(60)} columns={columns} rowKey={(r) => r.id} />
    );

    fireEvent.click(screen.getByText("Sonraki"));

    expect(screen.getByText(/Sayfa 2 \/ 3/)).toBeInTheDocument();
    expect(screen.getByText(/26–50 arası/)).toBeInTheDocument();
    expect(within(bodyRows()[0]).getByText("Kayıt 25")).toBeInTheDocument();
  });

  it("son sayfada Sonraki, ilk sayfada Önceki kapalıdır", () => {
    render(
      <DataTable rows={rows(30)} columns={columns} rowKey={(r) => r.id} />
    );

    expect(screen.getByText("Önceki")).toBeDisabled();

    fireEvent.click(screen.getByText("Sonraki"));
    expect(screen.getByText("Sonraki")).toBeDisabled();
    expect(screen.getByText("Önceki")).not.toBeDisabled();
  });

  it("sayfa boyutu değişince sayfa 1'e döner", () => {
    render(
      <DataTable rows={rows(300)} columns={columns} rowKey={(r) => r.id} />
    );

    fireEvent.click(screen.getByText("Sonraki"));
    expect(screen.getByText(/Sayfa 2 \/ 12/)).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Sayfa başına kayıt"), {
      target: { value: "100" },
    });

    expect(screen.getByText(/Sayfa 1 \/ 3/)).toBeInTheDocument();
    expect(bodyRows()).toHaveLength(100);
  });

  it("FİLTRE DEĞİŞİNCE SAYFA 1'E DÖNER", () => {
    const { rerender } = render(
      <DataTable
        rows={rows(300)}
        columns={columns}
        rowKey={(r) => r.id}
        resetKey="hepsi"
      />
    );

    fireEvent.click(screen.getByText("Sonraki"));
    fireEvent.click(screen.getByText("Sonraki"));
    expect(screen.getByText(/Sayfa 3 \/ 12/)).toBeInTheDocument();

    // Kullanıcı filtreyi daralttı: 300 satır 40'a düştü.
    rerender(
      <DataTable
        rows={rows(40)}
        columns={columns}
        rowKey={(r) => r.id}
        resetKey="daraltilmis"
      />
    );

    expect(screen.getByText(/Sayfa 1 \/ 2/)).toBeInTheDocument();
    expect(screen.getByText(/Toplam 40 kayıt/)).toBeInTheDocument();
  });

  it("sayfa sayısı küçülünce boş sayfada bırakmaz", () => {
    const { rerender } = render(
      <DataTable rows={rows(300)} columns={columns} rowKey={(r) => r.id} />
    );

    fireEvent.click(screen.getByText("Sonraki"));
    fireEvent.click(screen.getByText("Sonraki"));

    // resetKey YOK: yine de boş sayfada kalınmamalı.
    rerender(
      <DataTable rows={rows(30)} columns={columns} rowKey={(r) => r.id} />
    );

    expect(screen.getByText(/Sayfa 2 \/ 2/)).toBeInTheDocument();
    expect(bodyRows().length).toBeGreaterThan(0);
  });

  it("kayıt yoksa boş metni gösterir ve sayfalama iddiasında bulunmaz", () => {
    render(
      <DataTable
        rows={[]}
        columns={columns}
        rowKey={(r) => r.id}
        emptyText="Hiç fatura yok."
      />
    );

    expect(screen.getByText("Hiç fatura yok.")).toBeInTheDocument();
    expect(screen.getByText("Kayıt yok")).toBeInTheDocument();
  });
});

describe("DataTable — sunucu kipi", () => {
  it("toplam UÇTAN gelir, eldeki satırdan sayılmaz", () => {
    render(
      <DataTable
        rows={rows(25)}
        columns={columns}
        rowKey={(r) => r.id}
        server={{ total: 23531, page: 1, pageSize: 25, onChange: vi.fn() }}
      />
    );

    // Poz kütüphanesi: 25 satır elde, 23.531 kayıt var.
    expect(screen.getByText(/Toplam 23\.531 kayıt/)).toBeInTheDocument();
    expect(screen.getByText(/Sayfa 1 \/ 942/)).toBeInTheDocument();
  });

  it("sayfa değişimini ebeveyne bildirir, kendi dilimlemez", () => {
    const onChange = vi.fn();

    render(
      <DataTable
        rows={rows(25)}
        columns={columns}
        rowKey={(r) => r.id}
        server={{ total: 1000, page: 3, pageSize: 25, onChange }}
      />
    );

    fireEvent.click(screen.getByText("Sonraki"));
    expect(onChange).toHaveBeenCalledWith(4, 25);

    fireEvent.click(screen.getByText("Önceki"));
    expect(onChange).toHaveBeenCalledWith(2, 25);
  });

  it("TÜM KAYITLARI indirme, gerçekten verilebiliyorsa sunulur", () => {
    const { rerender } = render(
      <DataTable
        rows={rows(25)}
        columns={columns}
        rowKey={(r) => r.id}
        server={{ total: 5000, page: 1, pageSize: 25, onChange: vi.fn() }}
      />
    );

    // fetchAll yok: elde 25 satır var, 5000 kayıt sözü verilemez.
    expect(screen.queryByText("Tümünü İndir")).not.toBeInTheDocument();

    rerender(
      <DataTable
        rows={rows(25)}
        columns={columns}
        rowKey={(r) => r.id}
        server={{ total: 5000, page: 1, pageSize: 25, onChange: vi.fn() }}
        fetchAll={async () => rows(5000)}
      />
    );

    expect(screen.getByText("Tümünü İndir")).toBeInTheDocument();
  });
});

describe("DataTable — çıktı", () => {
  it("dışa aktarma düz değeri kullanır, ekrandaki süsü değil", () => {
    render(
      <DataTable
        rows={[{ id: "1", ad: "Beton", tutar: 1250 }]}
        columns={[
          {
            key: "ad",
            header: "Ad",
            // Ekranda rozet, dosyada düz metin.
            render: (row) => <strong>▲ {row.ad}</strong>,
            value: (row) => row.ad,
          },
          { key: "tutar", header: "Tutar", value: (row) => row.tutar },
        ]}
        rowKey={(r) => r.id}
        title="Stok Listesi"
      />
    );

    const { content, name } = captureDownload(() => {
      fireEvent.click(screen.getByText("Bu Sayfayı İndir"));
    });

    expect(content).toContain("Ad;Tutar");
    expect(content).toContain("Beton;1250");
    // Ekrandaki süs dosyaya sızmamalı.
    expect(content).not.toContain("▲");
    // Excel TR'nin UTF-8 tanıması için BOM.
    expect(content.startsWith("\ufeff")).toBe(true);
    // Dosya adı hangi listeye ve hangi sayfaya ait olduğunu söyler.
    expect(name).toBe("stok-listesi-sayfa-1.csv");
  });

  it("noktalı virgül ve tırnak içeren değerler kaçırılır", () => {
    render(
      <DataTable
        rows={[{ id: "1", ad: 'Kablo; 3x2,5 "NYA"', tutar: 5 }]}
        columns={columns}
        rowKey={(r) => r.id}
      />
    );

    const { content } = captureDownload(() => {
      fireEvent.click(screen.getByText("Bu Sayfayı İndir"));
    });

    expect(content).toContain('"Kablo; 3x2,5 ""NYA"""');
  });

  it("TÜMÜNÜ İNDİR eldeki sayfayı değil bütün kayıtları yazar", async () => {
    render(
      <DataTable
        rows={rows(25)}
        columns={columns}
        rowKey={(r) => r.id}
        server={{ total: 120, page: 1, pageSize: 25, onChange: vi.fn() }}
        fetchAll={async () => rows(120)}
      />
    );

    const originalBlob = globalThis.Blob;
    const originalCreate = URL.createObjectURL;
    const originalRevoke = URL.revokeObjectURL;
    const originalClick = HTMLAnchorElement.prototype.click;

    let content = "";
    class SpyBlob extends originalBlob {
      constructor(parts: BlobPart[], options?: BlobPropertyBag) {
        super(parts, options);
        content = String(parts[0]);
      }
    }

    globalThis.Blob = SpyBlob as unknown as typeof Blob;
    URL.createObjectURL = vi.fn(() => "blob:test");
    URL.revokeObjectURL = vi.fn();
    HTMLAnchorElement.prototype.click = function noop() {};

    try {
      fireEvent.click(screen.getByText("Tümünü İndir"));
      await waitFor(() => expect(content).not.toBe(""));

      // Başlık + 120 satır.
      expect(content.trim().split("\r\n")).toHaveLength(121);
    } finally {
      globalThis.Blob = originalBlob;
      URL.createObjectURL = originalCreate;
      URL.revokeObjectURL = originalRevoke;
      HTMLAnchorElement.prototype.click = originalClick;
    }
  });
});
