import { describe, expect, it } from "vitest";
import { sortByUrgency, terminDurumu, type TodoItem } from "@/services/todo.service";

/**
 * ACİLİYET SIRALAMASI.
 *
 * Sıralama TARİH DEĞİL ACİLİYET:
 *   1) Termini geçmiş
 *   2) Bugün biten
 *   3) Kalanlar: bekleme süresi uzun olan üstte
 *
 * Üçüncü ölçüt bilerek "en eski önce": en kolay unutulan iş, uzun
 * süredir bekleyendir.
 */

function kalem(over: Partial<TodoItem>): TodoItem {
  return {
    id: over.id ?? Math.random().toString(36).slice(2),
    kind: over.kind ?? "task",
    title: over.title ?? "Kalem",
    href: "#",
    isOverdue: over.isOverdue ?? false,
    isDueToday: over.isDueToday ?? false,
    waitingSince: over.waitingSince ?? null,
    ...over,
  };
}

describe("aciliyet siralamasi", () => {
  it("termini gecmis olanlar en uste cikar", () => {
    const siralanmis = sortByUrgency([
      kalem({ id: "normal" }),
      kalem({ id: "bugun", isDueToday: true }),
      kalem({ id: "gecmis", isOverdue: true }),
    ]);

    expect(siralanmis.map((x) => x.id)).toEqual(["gecmis", "bugun", "normal"]);
  });

  it("esit aciliyette EN ESKI BEKLEYEN uste cikar", () => {
    const siralanmis = sortByUrgency([
      kalem({ id: "yeni", waitingSince: "2026-08-20T10:00:00Z" }),
      kalem({ id: "eski", waitingSince: "2026-08-01T10:00:00Z" }),
      kalem({ id: "orta", waitingSince: "2026-08-10T10:00:00Z" }),
    ]);

    // En kolay unutulan iş, uzun süredir bekleyendir.
    expect(siralanmis.map((x) => x.id)).toEqual(["eski", "orta", "yeni"]);
  });

  it("gecmis olan, daha eski bekleyen normalden once gelir", () => {
    // ACİLİYET BEKLEME SÜRESİNİ EZER: yeni ama gecikmiş bir iş,
    // eski ama zamanı gelmemiş bir işten önemlidir.
    const siralanmis = sortByUrgency([
      kalem({ id: "eski-normal", waitingSince: "2026-01-01T00:00:00Z" }),
      kalem({ id: "yeni-gecmis", isOverdue: true, waitingSince: "2026-08-23T00:00:00Z" }),
    ]);

    expect(siralanmis[0].id).toBe("yeni-gecmis");
  });

  it("termini olmayan kalemler en sona duser", () => {
    const siralanmis = sortByUrgency([
      kalem({ id: "terminsiz" }),
      kalem({ id: "beklemede", waitingSince: "2026-08-01T00:00:00Z" }),
    ]);

    expect(siralanmis[0].id).toBe("beklemede");
  });
});

describe("termin durumu", () => {
  it("dun biten termin GECIKMIS sayilir", () => {
    const dun = new Date();
    dun.setDate(dun.getDate() - 1);

    expect(terminDurumu(dun.toISOString()).isOverdue).toBe(true);
  });

  it("bugun biten termin gecikmis DEGIL, bugun isaretli", () => {
    const durum = terminDurumu(new Date().toISOString());

    expect(durum.isOverdue).toBe(false);
    expect(durum.isDueToday).toBe(true);
  });

  it("yarin biten termin ikisi de degil", () => {
    const yarin = new Date();
    yarin.setDate(yarin.getDate() + 1);

    const durum = terminDurumu(yarin.toISOString());

    expect(durum.isOverdue).toBe(false);
    expect(durum.isDueToday).toBe(false);
  });

  it("termin yoksa gecikme de yok", () => {
    expect(terminDurumu(null)).toEqual({ isOverdue: false, isDueToday: false });
  });
});
