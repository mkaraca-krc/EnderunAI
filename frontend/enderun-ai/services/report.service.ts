async function downloadPdf(
  url: string,
  filename: string
) {
  const backendPath = url.replace(/^\/api\//, "");

  const response =
    await fetch(
      `/api/backend/${backendPath}`,
      {
        credentials: "include",
      }
    );

  if (!response.ok) {
    throw new Error(
      "PDF oluşturulamadı."
    );
  }

  const blob =
    await response.blob();

  const objectUrl =
    window.URL.createObjectURL(blob);

  const link =
    document.createElement("a");

  link.href = objectUrl;
  link.download = filename;

  document.body.appendChild(link);

  link.click();

  link.remove();

  window.URL.revokeObjectURL(objectUrl);
}


export const reportService = {

  downloadProgressPaymentPdf(
    id:string
  ) {
    return downloadPdf(
      `/api/reports/progress-payment/${id}/pdf`,
      `Hakediş-${id}.pdf`
    );
  },


  downloadPriceDifferencePdf(
    id:string
  ) {
    return downloadPdf(
      `/api/reports/price-difference/${id}/pdf`,
      `Fiyat-Farki-${id}.pdf`
    );
  },


  downloadDeductionPdf(
    id:string
  ) {
    return downloadPdf(
      `/api/reports/deductions/${id}/pdf`,
      `Kesinti-${id}.pdf`
    );
  },


  downloadStockIssuePdf(
    id: string
  ) {
    return downloadPdf(
      `/api/reports/stock-issue/${id}/pdf`,
      `Depo-Cikis-Fisi-${id}.pdf`
    );
  },


  downloadPurchaseOrderPdf(
    id: string
  ) {
    return downloadPdf(
      `/api/reports/purchase-order/${id}/pdf`,
      `Satinalma-Siparisi-${id}.pdf`
    );
  },

};
