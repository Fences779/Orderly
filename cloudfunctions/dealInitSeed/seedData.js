function iso(date) {
  return date.toISOString()
}

function daysAgo(days) {
  const date = new Date()
  date.setDate(date.getDate() - days)
  return iso(date)
}

function daysLater(days) {
  const date = new Date()
  date.setDate(date.getDate() + days)
  return iso(date)
}

function createSeed(workspaceId) {
  const createdBy = 'seed'
  const common = { workspaceId, createdBy, updatedBy: createdBy }
  const skus = [
    { _id: 'seed_sku_bracelet_basic', name: '基础天然石手串', category: '手串', basePrice: 168, costPrice: 68, specSchema: '手围/cm, 主色, 珠径/mm', adjustableFields: ['手围', '颜色', '材质'], tags: ['天然石', '入门价位'], enabled: true, sortOrder: 10 },
    { _id: 'seed_sku_bracelet_custom', name: '定制设计手串', category: '手串', basePrice: 328, costPrice: 120, specSchema: '手围/cm, 风格, 材质预算', adjustableFields: ['风格', '材质', '配件'], tags: ['定制', '高转化'], enabled: true, sortOrder: 20 },
    { _id: 'seed_sku_pearl_necklace', name: '淡水珍珠项链', category: '项链', basePrice: 268, costPrice: 110, specSchema: '长度/cm, 珍珠规格, 扣头', adjustableFields: ['长度', '扣头'], tags: ['珍珠', '礼物'], enabled: true, sortOrder: 30 },
    { _id: 'seed_sku_silver_accessory', name: '银饰小配件', category: '配件', basePrice: 88, costPrice: 32, specSchema: '款式, 尺寸, 是否刻字', adjustableFields: ['刻字', '尺寸'], tags: ['配件', '低客单'], enabled: true, sortOrder: 40 },
    { _id: 'seed_sku_gift_box', name: '礼盒包装升级', category: '礼物', basePrice: 38, costPrice: 15, specSchema: '礼盒颜色, 贺卡文案', adjustableFields: ['贺卡', '包装色'], tags: ['加购', '送人'], enabled: true, sortOrder: 50 },
    { _id: 'seed_sku_repair', name: '旧款调整/维修', category: '售后', basePrice: 58, costPrice: 18, specSchema: '维修内容, 材料补差', adjustableFields: ['工费', '材料'], tags: ['复购', '售后'], enabled: true, sortOrder: 60 }
  ].map(function(sku, index) {
    const stock = [24, 9, 16, 4, 38, 7][index] || 0
    const safety = [8, 6, 5, 6, 12, 4][index] || 0
    return Object.assign({
      purchasePrice: sku.costPrice,
      stockOnHand: stock,
      stockReserved: index % 2 === 0 ? 1 : 0,
      safetyStock: safety,
      stockUnit: '件',
      stockLocation: index < 3 ? 'A架' : 'B架',
      supplierName: index < 3 ? '主材供应商' : '配件供应商',
      inventoryRemark: '',
      reorderEnabled: true,
      lastRestockedAt: daysAgo(index + 1)
    }, sku)
  })

  const templates = [
    { _id: 'seed_tpl_clarify_1', sceneType: 'clarify', title: '尺寸追问', content: '亲爱的{{customerName}}，我先确认一下尺寸/手围，这样报价会更准，也能避免后面反复改。', variables: ['customerName'], toneStyle: '自然', enabled: true, useCount: 4 },
    { _id: 'seed_tpl_clarify_2', sceneType: 'clarify', title: '预算追问', content: '{{customerName}}，你这次大概想控制在什么预算段？我可以按预算给你搭两档方案。', variables: ['customerName'], toneStyle: '低打扰', enabled: true, useCount: 2 },
    { _id: 'seed_tpl_clarify_3', sceneType: 'clarify', title: '风格追问', content: '我看你偏向{{productName}}，想要更简约还是更有存在感一点？我按这个方向给你配。', variables: ['productName'], toneStyle: '自然', enabled: true, useCount: 1 },
    { _id: 'seed_tpl_quote_1', sceneType: 'quote_followup', title: '报价后轻跟进', content: '{{customerName}}，我刚才那版{{productName}}报价是{{quoteAmount}}，你不用急着定；如果有想调整预算或材质，我可以直接帮你改一版。', variables: ['customerName', 'productName', 'quoteAmount'], toneStyle: '低压', enabled: true, useCount: 8 },
    { _id: 'seed_tpl_quote_2', sceneType: 'quote_followup', title: '报价后补充价值', content: '补充说明一下，这版主要贵在材质和手工时间。如果你想压到更低预算，我可以保留风格，把材质换一档。', variables: [], toneStyle: '解释型', enabled: true, useCount: 5 },
    { _id: 'seed_tpl_quote_3', sceneType: 'quote_followup', title: '报价72小时提醒', content: '{{customerName}}，这版报价我先帮你保留到{{deadline}}。过期也能重新做，只是材料价格可能会变。', variables: ['customerName', 'deadline'], toneStyle: '提醒型', enabled: true, useCount: 3 },
    { _id: 'seed_tpl_shipping_1', sceneType: 'shipping', title: '发货通知', content: '{{customerName}}，你的{{productName}}已经发出啦，收到后如果尺寸或佩戴感有问题直接跟我说。', variables: ['customerName', 'productName'], toneStyle: '自然', enabled: true, useCount: 6 },
    { _id: 'seed_tpl_shipping_2', sceneType: 'shipping', title: '使用提醒', content: '收到后先别碰水和香水，日常放回密封袋会更耐用。', variables: [], toneStyle: '专业', enabled: true, useCount: 2 },
    { _id: 'seed_tpl_shipping_3', sceneType: 'shipping', title: '物流安抚', content: '物流如果有延迟我会帮你盯一下，有异常我这边先处理。', variables: [], toneStyle: '安抚', enabled: true, useCount: 1 },
    { _id: 'seed_tpl_aftersales_1', sceneType: 'aftersales', title: '收货关怀', content: '{{customerName}}，{{productName}}收到后佩戴感觉怎么样？尺寸、颜色和预期如果有偏差，我这边可以帮你看怎么调整。', variables: ['customerName', 'productName'], toneStyle: '关怀', enabled: true, useCount: 7 },
    { _id: 'seed_tpl_aftersales_2', sceneType: 'aftersales', title: '晒单引导', content: '如果你方便拍个佩戴图，我也能帮你看看搭配效果，后面再做同风格会更准。', variables: [], toneStyle: '轻引导', enabled: true, useCount: 2 },
    { _id: 'seed_tpl_aftersales_3', sceneType: 'aftersales', title: '售后承诺', content: '后续如果出现松动或小问题，先发图给我，我判断一下能不能免费处理。', variables: [], toneStyle: '专业', enabled: true, useCount: 1 },
    { _id: 'seed_tpl_repurchase_1', sceneType: 'repurchase', title: '复购触达', content: '{{customerName}}，你{{lastPurchaseDate}}买的那款风格最近有新材料了，如果想做同风格升级版，我可以给你留一组搭配。', variables: ['customerName', 'lastPurchaseDate'], toneStyle: '低打扰', enabled: true, useCount: 3 },
    { _id: 'seed_tpl_repurchase_2', sceneType: 'repurchase', title: '礼物节点', content: '最近快到生日/节日节点了，如果还想送人，我可以按上次的预算和风格直接给你配。', variables: [], toneStyle: '提醒', enabled: true, useCount: 2 },
    { _id: 'seed_tpl_repurchase_3', sceneType: 'repurchase', title: '老客优惠', content: '{{customerName}}，老客这次我可以帮你免一部分工费，想做的话我直接按上次档案开单。', variables: ['customerName'], toneStyle: '老客', enabled: true, useCount: 1 }
  ]

  const customers = [
    { _id: 'seed_customer_lin', name: '小林', platform: 'wechat', externalUid: 'wx_lin', contactHandle: 'lin_vx88', sourceChannel: '朋友圈', profileTags: ['喜欢简约', '珍珠'], preferenceNotes: '偏冷白和细链条', tabooNotes: '不喜欢太夸张', riskNotes: '', totalOrders: 2, totalSpent: 688, lastContactAt: daysAgo(1), lastPurchaseAt: daysAgo(38) },
    { _id: 'seed_customer_yuyu', name: '鱼鱼', platform: 'xianyu', externalUid: 'xy_yy', contactHandle: 'xy_yuyu', sourceChannel: '闲鱼搜索', profileTags: ['价格敏感'], preferenceNotes: '预算通常 200 内', tabooNotes: '', riskNotes: '容易比价', totalOrders: 0, totalSpent: 0, lastContactAt: daysAgo(0), lastPurchaseAt: '' },
    { _id: 'seed_customer_momo', name: 'Momo', platform: 'xiaohongshu', externalUid: 'xhs_momo', contactHandle: 'momo_note', sourceChannel: '小红书笔记', profileTags: ['礼物', '高级感'], preferenceNotes: '送人需求多', tabooNotes: '', riskNotes: '', totalOrders: 1, totalSpent: 368, lastContactAt: daysAgo(3), lastPurchaseAt: daysAgo(12) },
    { _id: 'seed_customer_chen', name: '陈小姐', platform: 'wechat', externalUid: 'wx_chen', contactHandle: 'chen_1024', sourceChannel: '老客转介绍', profileTags: ['急单'], preferenceNotes: '常要本周内发货', tabooNotes: '不接受延期', riskNotes: '时间敏感', totalOrders: 3, totalSpent: 1280, lastContactAt: daysAgo(2), lastPurchaseAt: daysAgo(20) },
    { _id: 'seed_customer_hao', name: '阿浩', platform: 'douyin', externalUid: 'dy_hao', contactHandle: 'hao_dy', sourceChannel: '抖音私信', profileTags: ['男款', '低调'], preferenceNotes: '偏黑银色', tabooNotes: '', riskNotes: '', totalOrders: 1, totalSpent: 258, lastContactAt: daysAgo(15), lastPurchaseAt: daysAgo(58) }
  ]

  const deals = [
    { _id: 'seed_deal_new', customerId: 'seed_customer_yuyu', title: '200内天然石手串', sourceEntry: 'xianyu', dealStage: 'new_inquiry', priorityLevel: 'medium', intentCategory: '手串', demandSummary: '想要 200 以内的天然石手串，先看看款式。', styleTags: ['低调'], materialTags: ['天然石'], sizeSpec: '手围 15cm', colorPref: '绿色', budgetMin: 0, budgetMax: 200, deadlineAt: '', urgencyLevel: 'low', latestQuoteId: '', nextFollowupAt: daysLater(1), lastInteractionAt: daysAgo(0), followupCount: 0, lossReason: '', archivedAt: '' },
    { _id: 'seed_deal_clarify', customerId: 'seed_customer_momo', title: '生日礼物项链', sourceEntry: 'xiaohongshu', dealStage: 'needs_clarification', priorityLevel: 'high', intentCategory: '礼物', demandSummary: '生日送人，想要高级但不要太成熟，需要补充预算和长度。', styleTags: ['高级', '温柔'], materialTags: ['珍珠'], sizeSpec: '', colorPref: '白色', budgetMin: 0, budgetMax: 0, deadlineAt: daysLater(6), urgencyLevel: 'medium', latestQuoteId: '', nextFollowupAt: daysLater(1), lastInteractionAt: daysAgo(1), followupCount: 1, lossReason: '', archivedAt: '' },
    { _id: 'seed_deal_quote_preparing', customerId: 'seed_customer_lin', title: '简约珍珠项链', sourceEntry: 'wechat', dealStage: 'quote_preparing', priorityLevel: 'medium', intentCategory: '项链', demandSummary: '想做简约珍珠项链，预算 300 左右，通勤佩戴。', styleTags: ['简约', '通勤'], materialTags: ['珍珠'], sizeSpec: '42cm', colorPref: '白色', budgetMin: 250, budgetMax: 350, deadlineAt: '', urgencyLevel: 'low', latestQuoteId: '', nextFollowupAt: '', lastInteractionAt: daysAgo(1), followupCount: 0, lossReason: '', archivedAt: '' },
    { _id: 'seed_deal_quote_sent', customerId: 'seed_customer_yuyu', title: '便宜点手串方案', sourceEntry: 'xianyu', dealStage: 'quote_sent', priorityLevel: 'medium', intentCategory: '手串', demandSummary: '报价后客户说先看看，需要轻跟进。', styleTags: ['低调'], materialTags: ['玛瑙'], sizeSpec: '手围 16cm', colorPref: '黑色', budgetMin: 120, budgetMax: 188, deadlineAt: '', urgencyLevel: 'low', latestQuoteId: 'seed_quote_1', nextFollowupAt: daysAgo(0), lastInteractionAt: daysAgo(2), followupCount: 1, lossReason: '', archivedAt: '' },
    { _id: 'seed_deal_scheduled', customerId: 'seed_customer_chen', title: '急单礼物手串', sourceEntry: 'wechat', dealStage: 'scheduled', priorityLevel: 'high', intentCategory: '礼物', demandSummary: '已收定金，安排本周制作。', styleTags: ['国风'], materialTags: ['朱砂'], sizeSpec: '手围 15.5cm', colorPref: '红色', budgetMin: 300, budgetMax: 500, deadlineAt: daysLater(4), urgencyLevel: 'high', latestQuoteId: 'seed_quote_2', nextFollowupAt: '', lastInteractionAt: daysAgo(1), followupCount: 0, lossReason: '', archivedAt: '' },
    { _id: 'seed_deal_production', customerId: 'seed_customer_chen', title: '银饰配件定制', sourceEntry: 'wechat', dealStage: 'in_production', priorityLevel: 'medium', intentCategory: '配件', demandSummary: '银饰小配件刻字，制作中。', styleTags: ['低调'], materialTags: ['银'], sizeSpec: '刻字 4 个字', colorPref: '银色', budgetMin: 80, budgetMax: 150, deadlineAt: daysLater(5), urgencyLevel: 'medium', latestQuoteId: '', nextFollowupAt: '', lastInteractionAt: daysAgo(3), followupCount: 0, lossReason: '', archivedAt: '' },
    { _id: 'seed_deal_shipped', customerId: 'seed_customer_momo', title: '礼盒珍珠项链', sourceEntry: 'xiaohongshu', dealStage: 'shipped', priorityLevel: 'medium', intentCategory: '礼物', demandSummary: '已发货，等待签收。', styleTags: ['高级'], materialTags: ['珍珠'], sizeSpec: '42cm', colorPref: '白色', budgetMin: 300, budgetMax: 420, deadlineAt: '', urgencyLevel: 'low', latestQuoteId: 'seed_quote_3', nextFollowupAt: daysLater(3), lastInteractionAt: daysAgo(1), followupCount: 0, lossReason: '', archivedAt: '' },
    { _id: 'seed_deal_completed', customerId: 'seed_customer_lin', title: '通勤珍珠项链旧单', sourceEntry: 'wechat', dealStage: 'completed', priorityLevel: 'medium', intentCategory: '项链', demandSummary: '已完成，可进入复购周期。', styleTags: ['简约'], materialTags: ['珍珠'], sizeSpec: '40cm', colorPref: '白色', budgetMin: 250, budgetMax: 320, deadlineAt: '', urgencyLevel: 'low', latestQuoteId: '', nextFollowupAt: daysLater(20), lastInteractionAt: daysAgo(38), followupCount: 1, lossReason: '', archivedAt: '' },
    { _id: 'seed_deal_repurchase', customerId: 'seed_customer_hao', title: '男款低调手串复购', sourceEntry: 'douyin', dealStage: 'repurchase_due', priorityLevel: 'medium', intentCategory: '手串', demandSummary: '上次男款手串已到复购提醒期。', styleTags: ['低调'], materialTags: ['檀木'], sizeSpec: '手围 17cm', colorPref: '黑色', budgetMin: 180, budgetMax: 260, deadlineAt: '', urgencyLevel: 'low', latestQuoteId: '', nextFollowupAt: daysAgo(0), lastInteractionAt: daysAgo(58), followupCount: 1, lossReason: '', archivedAt: '' },
    { _id: 'seed_deal_lost', customerId: 'seed_customer_yuyu', title: '比价后流失手串', sourceEntry: 'xianyu', dealStage: 'lost', priorityLevel: 'low', intentCategory: '手串', demandSummary: '客户觉得有点贵，最终流失。', styleTags: ['可爱'], materialTags: ['水晶'], sizeSpec: '', colorPref: '粉色', budgetMin: 0, budgetMax: 120, deadlineAt: '', urgencyLevel: 'low', latestQuoteId: '', nextFollowupAt: '', lastInteractionAt: daysAgo(5), followupCount: 2, lossReason: '价格敏感', archivedAt: '' }
  ]

  const quotes = [
    { _id: 'seed_quote_1', dealId: 'seed_deal_quote_sent', quoteNo: 'Q-SEED-001', quoteStatus: 'sent', items: [{ skuId: 'seed_sku_bracelet_basic', name: '基础天然石手串', qty: 1, price: 168 }], baseAmount: 168, customFee: 20, laborFee: 0, shippingFee: 8, discountAmount: 10, depositRequired: 80, totalAmount: 186, validUntil: daysLater(2), quoteNote: '可换黑色主珠', sentAt: daysAgo(2), respondedAt: '' },
    { _id: 'seed_quote_2', dealId: 'seed_deal_scheduled', quoteNo: 'Q-SEED-002', quoteStatus: 'accepted', items: [{ skuId: 'seed_sku_bracelet_custom', name: '定制设计手串', qty: 1, price: 328 }, { skuId: 'seed_sku_gift_box', name: '礼盒包装升级', qty: 1, price: 38 }], baseAmount: 366, customFee: 60, laborFee: 30, shippingFee: 0, discountAmount: 20, depositRequired: 200, totalAmount: 436, validUntil: daysLater(1), quoteNote: '急单优先排期', sentAt: daysAgo(3), respondedAt: daysAgo(2) },
    { _id: 'seed_quote_3', dealId: 'seed_deal_shipped', quoteNo: 'Q-SEED-003', quoteStatus: 'accepted', items: [{ skuId: 'seed_sku_pearl_necklace', name: '淡水珍珠项链', qty: 1, price: 268 }, { skuId: 'seed_sku_gift_box', name: '礼盒包装升级', qty: 1, price: 38 }], baseAmount: 306, customFee: 40, laborFee: 20, shippingFee: 0, discountAmount: 0, depositRequired: 150, totalAmount: 366, validUntil: daysAgo(2), quoteNote: '礼盒已包含贺卡', sentAt: daysAgo(10), respondedAt: daysAgo(9) }
  ]

  const inventoryMovements = [
    { _id: 'seed_inv_in_basic', skuId: 'seed_sku_bracelet_basic', skuName: '基础天然石手串', movementType: 'in', quantity: 20, unitCost: 68, totalCost: 1360, relatedOrderId: '', relatedOrderNo: '', operatorId: createdBy, note: 'seed 初始入库', occurredAt: daysAgo(6) },
    { _id: 'seed_inv_out_custom', skuId: 'seed_sku_bracelet_custom', skuName: '定制设计手串', movementType: 'out', quantity: 1, unitCost: 120, totalCost: 120, relatedOrderId: 'seed_deal_scheduled', relatedOrderNo: 'Q-SEED-002', operatorId: createdBy, note: 'seed 订单占用出库', occurredAt: daysAgo(2) },
    { _id: 'seed_inv_adjust_silver', skuId: 'seed_sku_silver_accessory', skuName: '银饰小配件', movementType: 'adjust', quantity: -2, unitCost: 32, totalCost: 64, relatedOrderId: '', relatedOrderNo: '', operatorId: createdBy, note: 'seed 盘点调整', occurredAt: daysAgo(1) }
  ]

  const cashflowEntries = [
    { _id: 'seed_cash_income_1', direction: 'income', amount: 200, category: '定金', paymentMethod: '微信支付', status: 'confirmed', relatedOrderId: 'seed_deal_scheduled', relatedOrderNo: 'Q-SEED-002', relatedQuoteId: 'seed_quote_2', relatedSkuId: 'seed_sku_bracelet_custom', counterpartyName: '陈小姐', operatorId: createdBy, note: '急单礼物手串定金', occurredAt: daysAgo(2) },
    { _id: 'seed_cash_income_2', direction: 'income', amount: 366, category: '尾款', paymentMethod: '微信支付', status: 'confirmed', relatedOrderId: 'seed_deal_shipped', relatedOrderNo: 'Q-SEED-003', relatedQuoteId: 'seed_quote_3', relatedSkuId: 'seed_sku_pearl_necklace', counterpartyName: 'Momo', operatorId: createdBy, note: '礼盒珍珠项链尾款', occurredAt: daysAgo(9) },
    { _id: 'seed_cash_expense_1', direction: 'expense', amount: 320, category: '材料采购', paymentMethod: '银行卡', status: 'confirmed', relatedOrderId: '', relatedOrderNo: '', relatedQuoteId: '', relatedSkuId: 'seed_sku_pearl_necklace', counterpartyName: '主材供应商', operatorId: createdBy, note: '珍珠批次补货', occurredAt: daysAgo(5) }
  ]

  const tasks = [
    { _id: 'seed_task_quote_reply', dealId: 'seed_deal_quote_sent', customerId: 'seed_customer_yuyu', customerName: '鱼鱼', triggerType: 'quote_no_reply', triggerAt: daysAgo(2), dueAt: daysAgo(1), priorityScore: 60, templateId: 'seed_tpl_quote_1', suggestedText: '鱼鱼，我刚才那版报价不用急着定；如果想压预算，我可以帮你换一档材质。', taskStatus: 'pending', completedAt: '', resultType: '', dedupeKey: 'seed_deal_quote_sent:quote_no_reply:seed_quote_1' },
    { _id: 'seed_task_post_delivery', dealId: 'seed_deal_shipped', customerId: 'seed_customer_momo', customerName: 'Momo', triggerType: 'post_delivery', triggerAt: daysAgo(1), dueAt: daysLater(2), priorityScore: 34, templateId: 'seed_tpl_aftersales_1', suggestedText: 'Momo，收到后佩戴感觉怎么样？尺寸和颜色如果有偏差可以直接跟我说。', taskStatus: 'pending', completedAt: '', resultType: '', dedupeKey: 'seed_deal_shipped:post_delivery:seed' },
    { _id: 'seed_task_repurchase', dealId: 'seed_deal_repurchase', customerId: 'seed_customer_hao', customerName: '阿浩', triggerType: 'repurchase', triggerAt: daysAgo(0), dueAt: daysAgo(0), priorityScore: 50, templateId: 'seed_tpl_repurchase_1', suggestedText: '阿浩，你上次买的男款低调风格最近有新材料了，要不要我给你留一组？', taskStatus: 'pending', completedAt: '', resultType: '', dedupeKey: 'seed_deal_repurchase:repurchase:seed' },
    { _id: 'seed_task_manual', dealId: 'seed_deal_clarify', customerId: 'seed_customer_momo', customerName: 'Momo', triggerType: 'manual', triggerAt: daysAgo(0), dueAt: daysLater(1), priorityScore: 42, templateId: 'seed_tpl_clarify_2', suggestedText: 'Momo，你这次大概想控制在什么预算段？我可以按预算给你搭两档方案。', taskStatus: 'pending', completedAt: '', resultType: '', dedupeKey: 'seed_deal_clarify:manual:seed' },
    { _id: 'seed_task_custom', dealId: 'seed_deal_scheduled', customerId: 'seed_customer_chen', customerName: '陈小姐', triggerType: 'custom', triggerAt: daysAgo(0), dueAt: daysLater(2), priorityScore: 45, templateId: '', suggestedText: '陈小姐，这单我会按急单排期，制作前再跟你确认一次细节。', taskStatus: 'pending', completedAt: '', resultType: '', dedupeKey: 'seed_deal_scheduled:custom:seed' }
  ]

  const captures = [
    { _id: 'seed_capture_clip_1', sourceType: 'clipboard', rawText: '鱼鱼：想要一个 200 以内的天然石手串，低调一点，手围 15cm，先看看。', rawImageUrl: '', ocrText: '', parserResult: { intentCategory: '手串', demandSummary: '想要一个 200 以内的天然石手串，低调一点，手围 15cm，先看看。', styleTags: ['低调'], materialTags: ['天然石'], budgetMax: 200, suggestedStage: 'needs_clarification' }, confidenceScore: 82, confirmStatus: 'confirmed', linkedCustomerId: 'seed_customer_yuyu', linkedDealId: 'seed_deal_new' },
    { _id: 'seed_capture_ocr_1', sourceType: 'ocr', rawText: '生日送人的珍珠项链，这周要，高级一点，预算 300 左右。', rawImageUrl: 'cloud://mock/ocr_1.jpg', ocrText: '生日送人的珍珠项链，这周要，高级一点，预算 300 左右。', parserResult: { intentCategory: '礼物', demandSummary: '生日送人的珍珠项链，这周要，高级一点，预算 300 左右。', styleTags: ['高级'], materialTags: ['珍珠'], budgetMin: 255, budgetMax: 345, suggestedStage: 'quote_preparing' }, confidenceScore: 88, confirmStatus: 'confirmed', linkedCustomerId: 'seed_customer_momo', linkedDealId: 'seed_deal_clarify' },
    { _id: 'seed_capture_draft', sourceType: 'clipboard', rawText: '我再想想，感觉有点贵，能便宜吗？', rawImageUrl: '', ocrText: '', parserResult: { demandSummary: '我再想想，感觉有点贵，能便宜吗？', riskFlags: [{ key: 'price_sensitive', label: '价格敏感' }], suggestedStage: 'needs_clarification' }, confidenceScore: 61, confirmStatus: 'draft', linkedCustomerId: '', linkedDealId: '' }
  ]

  function stamp(rows, extra) {
    return rows.map(function(row) {
      return Object.assign({}, common, row, extra || {}, {
        createdAt: row.createdAt || daysAgo(1),
        updatedAt: row.updatedAt || daysAgo(0)
      })
    })
  }

  return {
    sku_catalog: stamp(skus),
    inventory_movements: stamp(inventoryMovements),
    cashflow_entries: stamp(cashflowEntries),
    message_templates: stamp(templates),
    customers: stamp(customers),
    deals: stamp(deals),
    quotes: stamp(quotes, { workspaceId: workspaceId, createdBy, updatedBy: createdBy }),
    followup_tasks: stamp(tasks),
    captures: stamp(captures),
    activity_logs: [
      { _id: 'seed_log_quote', workspaceId, entityType: 'deal', entityId: 'seed_deal_quote_sent', actionType: 'quote_sent', beforeData: {}, afterData: { quoteId: 'seed_quote_1' }, note: 'seed 报价发送', operatorId: 'seed', createdAt: daysAgo(2) },
      { _id: 'seed_log_stage', workspaceId, entityType: 'deal', entityId: 'seed_deal_scheduled', actionType: 'stage_update', beforeData: { dealStage: 'waiting_deposit' }, afterData: { dealStage: 'scheduled' }, note: 'seed 成交排期', operatorId: 'seed', createdAt: daysAgo(2) },
      { _id: 'seed_log_lost', workspaceId, entityType: 'deal', entityId: 'seed_deal_lost', actionType: 'stage_update', beforeData: { dealStage: 'quote_sent' }, afterData: { dealStage: 'lost' }, note: '价格敏感流失', operatorId: 'seed', createdAt: daysAgo(5) }
    ]
  }
}

module.exports = {
  createSeed
}
