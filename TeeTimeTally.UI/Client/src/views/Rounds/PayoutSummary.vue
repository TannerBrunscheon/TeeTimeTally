<script setup lang="ts">
import { computed } from 'vue';
import type { PropType } from 'vue';
import type { GetRoundByIdResponse } from '@/models/round';

const props = defineProps({
    round: {
        type: Object as PropType<GetRoundByIdResponse>,
        required: true
    }
});

const formatCurrency = (value: number | undefined | null) => {
    return (value ?? 0).toLocaleString('en-US', { style: 'currency', currency: 'USD' });
};

const sortedPayouts = computed(() => {
    if (!props.round.playerPayouts) return [];
    // Sort players by total winnings, descending
    return [...props.round.playerPayouts].sort((a, b) => b.totalWinnings - a.totalWinnings);
});

</script>
<template>
    <div class="card border-success">
         <div class="card-header bg-success text-white">
            <h3><i class="bi bi-cash-stack me-2"></i>Payout Summary</h3>
        </div>
        <div class="card-body">
            <div class="row text-center">
                <div class="col-md-3 mb-3">
                    <h5 class="text-muted">Total Pot</h5>
                    <p class="fs-4 fw-bold">{{ formatCurrency(round.totalPot) }}</p>
                </div>
                <div class="col-md-3 mb-3">
                    <h5 class="text-muted">Skins Payout</h5>
                    <p class="fs-4 fw-bold">{{ formatCurrency(round.finalTotalSkinsPayout) }}</p>
                </div>
                 <div class="col-md-3 mb-3">
                    <h5 class="text-muted">CTH Payout</h5>
                    <p class="fs-4 fw-bold">{{ formatCurrency(round.financials.perRoundCalculatedCthPayout) }}</p>
                </div>
                 <div class="col-md-3 mb-3">
                    <h5 class="text-muted">Overall Winner(s)</h5>
                    <p class="fs-4 fw-bold">{{ formatCurrency(round.finalOverallWinnerPayoutAmount) }}</p>
                </div>
            </div>
             <hr />
            <h4 class="mb-3">Player Winnings</h4>
            <div class="table-responsive">
                <table class="table table-striped table-hover">
                    <thead class="table-light">
                        <tr>
                            <th>Player</th>
                            <th>Team</th>
                            <th class="text-end">Skins</th>
                            <th class="text-end">CTH</th>
                            <th class="text-end">Overall</th>
                            <th class="text-end">Total Winnings</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="payout in sortedPayouts" :key="payout.golferId">
                            <td>{{ payout.fullName }}</td>
                            <td><small class="text-muted">{{ payout.teamName }}</small></td>
                            <td class="text-end">{{ formatCurrency(payout.breakdown.skinsWinnings) }}</td>
                            <td class="text-end">{{ formatCurrency(payout.breakdown.cthWinnings) }}</td>
                            <td class="text-end">{{ formatCurrency(payout.breakdown.overallWinnings) }}</td>
                            <td class="text-end fw-bold">{{ formatCurrency(payout.totalWinnings) }}</td>
                        </tr>
                    </tbody>
                </table>
            </div>
            <div v-if="round.finalSkinRolloverAmount && round.finalSkinRolloverAmount > 0" class="alert alert-warning mt-3">
                <strong>Rollover:</strong> {{ formatCurrency(round.finalSkinRolloverAmount) }} from skins was not won and rolled over.
            </div>
             <div v-if="round.payoutVerificationMessage" class="alert alert-secondary mt-3">
                <small>{{ round.payoutVerificationMessage }}</small>
             </div>
        </div>
    </div>
</template>
