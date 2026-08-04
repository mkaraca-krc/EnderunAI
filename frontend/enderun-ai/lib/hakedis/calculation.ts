/**
 * Hakediş hesabının arayüz tarafı.
 *
 * Sunucudaki HakedisCalculationService'in birebir eşi: kullanıcı girdikçe
 * anlık toplam görsün diye. Kaydedilen değerler yine sunucuda yeniden
 * hesaplanır — burası yalnızca önizleme, tek doğruluk kaynağı sunucudur.
 *
 * Yuvarlama sunucudaki gibi 2 hane ve yarımlar yukarı (AwayFromZero).
 */

export function round2(value: number) {
  const scaled = value * 100;
  // JavaScript'in Math.round'u -0.5'i yukarı yuvarlar; sunucudaki
  // AwayFromZero ile aynı davranması için işaret ayrıca uygulanır.
  const rounded = Math.sign(scaled) * Math.round(Math.abs(scaled));
  return rounded / 100;
}

export type ItemInput = {
  sectionId: string | null;
  contractQuantity: number;
  previousQuantity: number;
  currentQuantity: number;
  materialUnitPrice: number;
  laborUnitPrice: number;
  overheadUnitPrice: number;
};

export type ItemResult = {
  unitPrice: number;
  cumulativeQuantity: number;
  materialAmount: number;
  laborAmount: number;
  overheadAmount: number;
  previousAmount: number;
  currentAmount: number;
  cumulativeAmount: number;
  completionRate: number;
  exceedsContractQuantity: boolean;
};

export function calculateItem(input: ItemInput): ItemResult {
  const previous = Math.max(0, input.previousQuantity || 0);
  const current = Math.max(0, input.currentQuantity || 0);
  const contract = Math.max(0, input.contractQuantity || 0);

  const material = Math.max(0, input.materialUnitPrice || 0);
  const labor = Math.max(0, input.laborUnitPrice || 0);
  const overhead = Math.max(0, input.overheadUnitPrice || 0);
  const unitPrice = round2(material + labor + overhead);

  const cumulativeQuantity = previous + current;

  return {
    unitPrice,
    cumulativeQuantity,
    materialAmount: round2(current * material),
    laborAmount: round2(current * labor),
    overheadAmount: round2(current * overhead),
    previousAmount: round2(previous * unitPrice),
    currentAmount: round2(current * unitPrice),
    cumulativeAmount: round2(cumulativeQuantity * unitPrice),
    completionRate: contract > 0 ? round2((cumulativeQuantity / contract) * 100) : 0,
    exceedsContractQuantity: contract > 0 && cumulativeQuantity > contract,
  };
}

/** İhzarat tutarı: miktar × birim fiyat × bedellendirme oranı. */
export function calculateAdvanceMaterial(
  quantity: number,
  unitPrice: number,
  valuationRate: number
) {
  const q = Math.max(0, quantity || 0);
  const price = Math.max(0, unitPrice || 0);
  const rate = Math.min(100, Math.max(0, valuationRate || 0));

  return round2((q * price * rate) / 100);
}

export type DeductionLineInput = {
  unitPrice: number;
  quantity: number;
  vatRate: number;
};

export function calculateDeductionLine(line: DeductionLineInput) {
  const unitPrice = Math.max(0, line.unitPrice || 0);
  const quantity = Math.max(0, line.quantity || 0);
  const vatRate = Math.max(0, line.vatRate || 0);

  const net = round2(unitPrice * quantity);
  const vat = round2((net * vatRate) / 100);

  return { netAmount: net, vatAmount: vat, grossAmount: round2(net + vat) };
}

export type DeductionInput = {
  rate: number;
  cumulativeBaseAmount: number;
  previousAmount: number;
  manualAmount: number | null;
  lines: DeductionLineInput[];
};

export type DeductionResult = {
  amount: number;
  cumulativeAmount: number;
  previousAmount: number;
  cumulativeBaseAmount: number;
  lineTotal: number;
};

/**
 * Kümülatif kesinti = kümülatif taban × oran; bu dönem = kümülatif −
 * önceki. Alt kalem varsa tutar oranla değil kalemlerden gelir.
 */
export function calculateDeduction(input: DeductionInput): DeductionResult {
  const previous = Math.max(0, round2(input.previousAmount || 0));

  if (input.lines.length > 0) {
    const lineTotal = round2(
      input.lines.reduce(
        (sum, line) => sum + calculateDeductionLine(line).grossAmount,
        0
      )
    );

    return {
      amount: lineTotal,
      cumulativeAmount: round2(previous + lineTotal),
      previousAmount: previous,
      cumulativeBaseAmount: 0,
      lineTotal,
    };
  }

  if (input.manualAmount !== null && input.manualAmount !== undefined) {
    const amount = Math.max(0, round2(input.manualAmount));

    return {
      amount,
      cumulativeAmount: round2(previous + amount),
      previousAmount: previous,
      cumulativeBaseAmount: round2(input.cumulativeBaseAmount || 0),
      lineTotal: 0,
    };
  }

  const base = Math.max(0, round2(input.cumulativeBaseAmount || 0));
  const cumulative = round2((base * (input.rate || 0)) / 100);

  // Kümülatif kesinti önceki toplamın altına düşerse geri ödeme yapılmaz.
  const amount = Math.max(0, round2(cumulative - previous));

  return {
    amount,
    cumulativeAmount: round2(previous + amount),
    previousAmount: previous,
    cumulativeBaseAmount: base,
    lineTotal: 0,
  };
}

export type HeaderInput = {
  cumulativeWorkAmount: number;
  cumulativeAdvanceMaterialAmount: number;
  previousTotalAmount: number;
  priceDifferenceAmount: number;
  vatRate: number;
  withholdingNumerator: number;
  withholdingDenominator: number;
  incomeTaxWithholdingRate: number;
  totalDeductionAmount: number;
};

export type HeaderResult = {
  cumulativeTotalAmount: number;
  currentAmount: number;
  taxableAmount: number;
  vatAmount: number;
  withholdingAmount: number;
  declaredVatAmount: number;
  incomeTaxWithholdingAmount: number;
  grossPayableAmount: number;
  netPayableAmount: number;
};

/**
 * Üst hesap: kümülatif imalat + açık ihzarat → minha → bu hakediş →
 * KDV → tevkifat → stopaj → kesintiler → tahsil edilecek.
 */
export function calculateHeader(input: HeaderInput): HeaderResult {
  const work = round2(input.cumulativeWorkAmount || 0);
  const advance = round2(input.cumulativeAdvanceMaterialAmount || 0);
  const cumulativeTotal = round2(work + advance);

  const previousTotal = round2(input.previousTotalAmount || 0);
  const currentAmount = round2(cumulativeTotal - previousTotal);

  const priceDifference = round2(input.priceDifferenceAmount || 0);
  const taxableAmount = round2(currentAmount + priceDifference);

  const vatAmount = round2((taxableAmount * (input.vatRate || 0)) / 100);

  const withholdingAmount =
    input.withholdingDenominator > 0
      ? round2(
          (vatAmount * input.withholdingNumerator) / input.withholdingDenominator
        )
      : 0;

  const incomeTaxWithholdingAmount =
    input.incomeTaxWithholdingRate > 0
      ? round2((taxableAmount * input.incomeTaxWithholdingRate) / 100)
      : 0;

  const gross = round2(taxableAmount + vatAmount);
  const deductions = round2(input.totalDeductionAmount || 0);

  return {
    cumulativeTotalAmount: cumulativeTotal,
    currentAmount,
    taxableAmount,
    vatAmount,
    withholdingAmount,
    declaredVatAmount: round2(vatAmount - withholdingAmount),
    incomeTaxWithholdingAmount,
    grossPayableAmount: gross,
    netPayableAmount: round2(
      gross - withholdingAmount - incomeTaxWithholdingAmount - deductions
    ),
  };
}

export type PaymentPlanInput = {
  paymentType: number;
  rate: number;
  maturityDays: number | null;
};

/**
 * Tahsil edilecek tutarı parçalara böler. Yuvarlama farkı SON parçaya
 * yazılır; aksi halde parçaların toplamı tahsil edilecek tutardan kuruş
 * sapardı.
 */
export function calculatePaymentPlan(
  netPayableAmount: number,
  progressPaymentDate: string,
  parts: PaymentPlanInput[]
) {
  const net = round2(netPayableAmount || 0);
  let allocated = 0;

  return parts.map((part, index) => {
    const isLast = index === parts.length - 1;
    const amount = isLast
      ? round2(net - allocated)
      : round2((net * (part.rate || 0)) / 100);

    allocated = round2(allocated + amount);

    let dueDate: string | null = null;

    if (part.maturityDays && part.maturityDays > 0 && progressPaymentDate) {
      const date = new Date(progressPaymentDate);
      date.setDate(date.getDate() + part.maturityDays);
      dueDate = date.toISOString().slice(0, 10);
    }

    return { ...part, amount, dueDate };
  });
}

/** Oranlar %100 etmiyorsa veya vadesiz çek varsa hata mesajı döner. */
export function validatePaymentPlan(parts: PaymentPlanInput[]) {
  if (parts.length === 0) return null;

  if (parts.some((x) => (x.rate || 0) < 0)) {
    return "Ödeme dağılım oranı negatif olamaz.";
  }

  const total = round2(parts.reduce((sum, x) => sum + (x.rate || 0), 0));

  if (total !== 100) {
    return `Ödeme dağılım oranlarının toplamı %100 olmalıdır (şu an %${total.toLocaleString(
      "tr-TR"
    )}).`;
  }

  if (
    parts.some(
      (x) => x.paymentType === 1 && (!x.maturityDays || x.maturityDays <= 0)
    )
  ) {
    return "Vadeli çek parçasında vade gün sayısı zorunludur.";
  }

  return null;
}

/** Kesinti türleri — sunucudaki HakedisDeductionType ile aynı değerler. */
export const DeductionType = {
  Other: 0,
  PerformanceBond: 1,
  AllRiskInsurance: 2,
  MaterialDeduction: 3,
  Barter: 4,
  Meal: 5,
  Accommodation: 6,
  OhsPenalty: 7,
  OhsContribution: 8,
} as const;

export const DEDUCTION_TYPE_OPTIONS: Array<{
  value: number;
  label: string;
  /** Alt kalemli mi (birim × adet × KDV) */
  hasLines: boolean;
  defaultRate: number;
  defaultLines?: string[];
}> = [
  { value: DeductionType.PerformanceBond, label: "Kesin teminat", hasLines: false, defaultRate: 5 },
  { value: DeductionType.AllRiskInsurance, label: "All-risk sigorta", hasLines: false, defaultRate: 0.5 },
  { value: DeductionType.MaterialDeduction, label: "Malzeme kesintisi", hasLines: false, defaultRate: 10 },
  { value: DeductionType.Barter, label: "Barter", hasLines: false, defaultRate: 40 },
  {
    value: DeductionType.Meal,
    label: "Yemek",
    hasLines: true,
    defaultRate: 0,
    defaultLines: ["Kahvaltı", "Öğlen", "Akşam", "Kumanya"],
  },
  {
    value: DeductionType.Accommodation,
    label: "Konaklama / kamp",
    hasLines: true,
    defaultRate: 0,
    defaultLines: ["Yatılı", "Evci"],
  },
  { value: DeductionType.OhsPenalty, label: "İSG ceza", hasLines: true, defaultRate: 0, defaultLines: ["İSG ceza"] },
  { value: DeductionType.OhsContribution, label: "İSG katılımı", hasLines: true, defaultRate: 0, defaultLines: ["İSG katılım payı"] },
  { value: DeductionType.Other, label: "Diğer kesinti", hasLines: false, defaultRate: 0.3 },
];
