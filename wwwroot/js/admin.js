// Admin dashboard client-side helpers

window.calculatePayout = function() {
    const amountInput = document.getElementById('betAmountInput');
    const oddsInput = document.getElementById('betOddsInput');
    const payoutDisplay = document.getElementById('payoutDisplay');
    const liabilityDisplay = document.getElementById('liabilityDisplay');

    if (amountInput && oddsInput) {
        const amount = parseFloat(amountInput.value) || 0;
        const odds = parseFloat(oddsInput.value) || 0;
        
        if (payoutDisplay) {
            const payout = amount * odds;
            payoutDisplay.innerText = payout.toLocaleString('no-NO', { maximumFractionDigits: 0 }) + ' kr';
        }
        if (liabilityDisplay) {
            const liability = Math.floor(amount * (Math.max(1, odds) - 1));
            liabilityDisplay.innerText = liability.toLocaleString('no-NO', { maximumFractionDigits: 0 }) + ' kr';
        }
    }
};

window.downloadCsv = function(filename, content) {
    const blob = new Blob([content], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement("a");
    if (link.download !== undefined) {
        const url = URL.createObjectURL(blob);
        link.setAttribute("href", url);
        link.setAttribute("download", filename);
        link.style.visibility = 'hidden';
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(url);
    }
};

// Global key listener to blur inputs and close review modal on Escape
document.addEventListener('keydown', function(event) {
    if (event.key === 'Escape') {
        const modal = document.querySelector('.review-modal-content');
        if (modal) {
            const activeEl = document.activeElement;
            if (activeEl && (activeEl.tagName === 'INPUT' || activeEl.tagName === 'TEXTAREA')) {
                activeEl.blur(); // Forces onchange event to be sent to Blazor Server
            }
            
            setTimeout(() => {
                const closeBtn = modal.querySelector('.btn-close');
                if (closeBtn) closeBtn.click();
            }, 100);
        }
    }
});
