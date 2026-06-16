/**
* Lightweight i18n — Machine Downtime System
* All translations bundled inline (no CDN / HTTP fetch required).
*
* HTML usage:
*   <span data-i18n="nav.logout">Logout</span>
*   <input data-i18n-ph="login.usernamePH" placeholder="Enter username..." />
*   <button data-i18n-title="common.save" title="Save">…</button>
*
* JS usage:
*   window.i18nT('nav.createDowntime')
*   window.changeLang('cn')
*   window.getLang()
*/
(function (global) {
  'use strict';
  
  var LANG_KEY = 'dtapp-lang';
  
  /* ─────────────────────────────────────────────────────────────
  TRANSLATIONS
  ───────────────────────────────────────────────────────────── */
  var dict = {
    
    /* ══════════════════════════ ENGLISH ══════════════════════════ */
    en: {
      common: {
        save: 'Save', cancel: 'Cancel', close: 'Close', confirm: 'Confirm',
        delete: 'Delete', loading: 'Loading…', search: 'Search',
        filter: 'Filter', reset: 'Reset', export: 'Export',
        noData: 'No data found', all: 'All', from: 'From', to: 'To',
        status: 'Status', date: 'Date', location: 'Location',
        operation: 'Operation / Line', machine: 'Machine', employee: 'Employee',
        description: 'Description', action: 'Action', total: 'Total',
        prev: 'Previous', next: 'Next', page: 'Page', of: 'of',
        yes: 'Yes', no: 'No', apply: 'Apply', clear: 'Clear',
        exportExcel: '📥 Export Excel', submit: 'Submit', skip: 'Skip',
        markAllRead: 'Mark All as Read', searchPlaceholder: 'Search…',
        unread: 'Unread', pageSize: 'per page', showing: 'Showing',
        hidden: 'hidden', items: 'items', confirmDelete: 'Confirm Delete',
        deleteFailed: 'Delete failed', error: 'Error', connectionError: 'Connection error',
        enableSoundPrompt: 'Enable sound for notifications', enableSound: 'Enable Sound',
        today: 'Today', handling: 'Handling', lastUpdated: 'Last updated',
        refresh: 'Refresh', errorLoad: 'Error loading data', retry: 'Retry',
        reconnecting: 'Reconnecting…', connected: 'Connected', disconnected: 'Disconnected'
      },
      login: {
        brand: 'SIGMA WORLDWIDE',
        systemLine1: 'Machine', systemLine2: 'Downtime', systemLine3: 'System',
        tagline: 'The system is used to manage equipment downtime during the production process.',
        statusProd: 'Real-time production tracking',
        statusTech: 'Technical response management',
        statusReport: 'Downtime analytics & reporting',
        signIn: 'Sign in',
        signInPrompt: 'Please enter your credentials to continue',
        username: 'Username', usernamePH: 'Enter username…',
        password: 'Password', passwordPH: 'Enter password…',
        loginBtn: 'Login →',
        footer: 'Sigma Worldwide · Downtime System'
      },
      nav: {
        createDowntime: 'Create Downtime',
        downtimeHistory: 'Downtime History',
        downtimeReport: 'Downtime Report',
        notifDashboard: 'Notification Dashboard',
        myNotifications: 'My Notifications',
        adminPanel: 'Admin Panel',
        driDashboard: 'DRI Dashboard',
        logout: 'Logout',
        techResponses: '📬 Tech Team Responses',
        prodNotifs: '🛑 Production Notifications',
        allNotifs: '📋 All Notifications',
        clearAll: 'Clear All',
        noNotifs: 'No notifications.'
      },
      createDowntime: {
        pageTitle: 'Downtime Input',
        subtitle: 'Record a new downtime event',
        location: 'Location', selectLocation: '-- Select location --',
        operation: 'Operation', selectOperation: '-- Select operation --',
        machine: 'Machine / Fixture No.',
        searchMachine: 'Search machine…', selectMachine: '-- Select machine/fixture no. --',
        employee: 'Employee', selectEmployee: '-- Select employee --',
        station: 'Station', stationPH: 'Enter station…',
        category: 'Category', selectCategory: '-- Select category --',
        effect: 'Effect on Production', selectEffect: '-- Select effect --',
        affectsProduction: 'Affects Production', noEffect: 'No Effect',
        description: 'Problem Description', descriptionPH: 'Describe the problem…',
        image: 'Image (optional)',
        callBtn: '🛑 Call', responseBtn: '⚙ Response', runBtn: '▶ Run',
        rootCause: 'Root Cause', rootCausePH: 'Enter root cause…',
        actionTaken: 'Action Taken', actionTakenPH: 'Describe actions taken…',
        spareParts: 'Spare Parts Used', sparePartsPH: 'List spare parts used…',
        uploadImage: 'Upload Image', removeImage: 'Remove', scanBarcode: 'Scan Barcode / QR',
        confirmSave: 'Confirm saving downtime?'
      },
      notifDashboard: {
        pageTitle: 'Technical Dashboard',
        subtitle: 'Real-time stop notifications',
        connecting: 'Connecting…', connected: 'Connected', disconnected: 'Disconnected',
        totalCalls: 'Total Calls', pending: 'Pending', accepted: 'Accepted',
        rejected: 'Rejected', resolved: 'Resolved', waiting: 'Waiting',
        respond: '✅ Response', reject: '❌ Reject', notEF: '🏢 Not E&F',
        fixComplete: '🔧 Respond — Fix Complete',
        rejectTitle: '❌ Reject Maintenance Request',
        device: 'Device', line: 'Line',
        rejectPH: 'Please enter rejection reason… (Required)',
        confirmReject: '❌ Confirm Rejection',
        postRunTitle: '📋 Add Repair Details',
        postRunSubtitle: 'Please fill in before notifying Production to resume.',
        saveDetails: '💾 Save', skipDetails: 'Skip',
        category: 'Category', selectCategory: '-- Select category --',
        effect: 'Effect', rootCause: 'Root Cause', rootCausePH: 'Enter root cause…',
        actionTaken: 'Action Taken', actionTakenPH: 'Describe actions taken…',
        spareParts: 'Spare Parts Used', sparePartsPH: 'List spare parts used…',
        postRunDesc: 'Problem Description', descPH: 'Problem description…',
        selectTechnician: '-- Select Technician --',
        charCount: 'characters'
      },
      prodNotifs: {
        pageTitle: 'Production Notifications',
        subtitle: 'Responses from the technical team',
        totalNotifs: 'Total', accepted: 'Accepted', waiting: 'Waiting', fixComplete: 'Fix Complete',
        allTab: 'All', acceptTab: '✅ Accepted', waitTab: '⏳ Waiting', fixTab: '🔧 Fix Complete',
        noNotifs: 'No notifications for the selected filter.',
        callDRI: '📞 Call DRI', runMachine: '▶ Run Machine',
        machine: 'Machine', line: 'Line', employee: 'Employee',
        techResponse: 'Tech Response', fixedBy: 'Fixed by', runningMsg: 'Production resumed'
      },
      downtimeList: {
        pageTitle: 'Downtime History', filters: 'Filters',
        fromDate: 'From Date', toDate: 'To Date',
        operation: 'Line', location: 'Location', machine: 'Machine',
        station: 'Station', employee: 'Employee', reason: 'Reason', effect: 'Effect',
        applyFilter: '🔍 Filter', resetFilter: '✕ Clear', exportExcel: '📥 Export Excel',
        noData: 'No records found.', prevPage: '← Prev', nextPage: 'Next →',
        colDatetime: 'Datetime', colOperation: 'Operation', colMachine: 'Machine / Fixture',
        colLocation: 'Location', colState: 'State', colStation: 'Station',
        colReason: 'Category', colEffect: 'Effect', colDescription: 'Description',
        colAction: 'Action', colRootCause: 'Root Cause', colSpareParts: 'Spare Parts',
        colEmployee: 'Employee', showRecords: 'records per page',
        machineFixture: 'Machine & Fixture', efNo: 'E & F no.',
        employee_name: 'Employee Name', downtime_start_time: 'Downtime Start Time'
      },
      report: {
        pageTitle: 'Downtime Report',
        apply: '🔍 Apply', reset: '✕ Reset', exportExcel: '📥 Export Excel',
        top5Title: '🏆 Top 5 — Most Downtime Machines',
        top5ExportBtn: 'Top 5 Downtime by Time Period',
        byLine: 'By Line', byMachine: 'By Machine',
        summaryLine: 'Summary by Line', summaryMachine: 'Summary by Machine',
        downtimeCount: 'Downtime Count', totalDowntime: 'Total Downtime', totalMinutes: 'Total (min)',
        operation: 'Line', location: 'Location', machine: 'Machine',
        fromDate: 'From Date', toDate: 'To Date', effect: 'Effect',
        stopPeriod: 'STOP(s) — period', stopMonth: 'STOP(s) — month',
        avgRepair: 'Response Duration', downtimeMin: 'Downtime (min)', repairCount: 'Repair Count',
        mttr: 'MTTR — Mean Time To Repair (min)',
        recordsPerPage: 'Records per page', activeFilters: 'Active Filters',
        totalLines: 'Total Lines', totalDowntimeEvents: 'Total Downtime Events',
        affectedMachines: 'Affected Machines',
        downtimeDistribution: 'Downtime Distribution', downtimeByReason: 'Downtime by Category',
        filtercategory: 'Category',
        reason: 'Category', employeeName: 'Technical Name',
        dailyTrend: 'Daily Downtime Trend', responseTimeByTech: 'Response Time by Technician',
        detailByLine: 'Detail by Line', machineDetail: 'Machine Detail',
        detailRecords: 'Detail Records', searchPlaceholder: 'Search records…',
        noResponseRecords: 'No response records found', noMachineData: 'No machine data',
        noRecordsMatch: 'No records match the current filter', noDowntimeData: 'No downtime data',
        noFaultCodeData: 'No fault code data available'
      },
      admin: {
        pageTitle: 'Admin Panel', subtitle: 'System Administration', badge: 'ADMIN',
        usersTab: '👤 User Management', logsTab: '📋 Notification Logs', settingsTab: '⚙ Settings',
        addUser: '+ Add User', username: 'Username', password: 'Password',
        role: 'Role', selectRole: '-- Select Role --', saveUser: 'Save User', deleteUser: 'Delete',
        filterDate: 'Filter by Date', filterType: 'Filter by Type', noLogs: 'No logs found.',
        colDatetime: 'Datetime', colType: 'Type', colMessage: 'Message',
        colMachine: 'Machine', colEmployee: 'Employee',
        downtimeTab: 'Downtime Records', employeeTab: 'Employee Management', accountTab: 'Account Management',
        filterOperation: 'Operation', filterState: 'State', filterFrom: 'From', filterTo: 'To',
        allStates: 'All States', colId: 'ID', colState: 'State', colLocation: 'Location',
        colReason: 'Category', colEffect: 'Effect', colStation: 'Station',
        colDescription: 'Description', colAction: 'Action',
        filterEmpId: 'Employee ID', addEmployee: 'Add Employee',
        colEmpId: 'Employee ID', colChineseName: 'Chinese Name', colEnglishName: 'English Name',
        addAccount: 'Add Account', resetPassword: 'Reset Password',
        accountLabel: 'Account', newPassword: 'New Password',
        generateBtn: 'Generate', generateTip: 'Password will be generated randomly',
        confirmReset: 'Confirm Reset', confirmDeleteTitle: 'Confirm Delete',
        confirmDeleteMsg: 'Are you sure you want to delete this item?',
        filterOperationPh: 'Filter by operation…', filterEmpIdPh: 'Search by employee ID…',
        filterAccPh: 'Search by username…', passwordPh: 'Enter password…',
        newPasswordPh: 'Enter new password…', edit : 'Edit', resetPwBtn: 'Reset Password'
      },
      dri: {
        pageTitle: 'DRI Dashboard', subtitle: 'Department Representative Interface',
        accept: 'Accept', resolve: 'Mark Resolved', escalate: 'Escalate',
        pending: 'Pending', resolved: 'Resolved', noPending: 'No pending DRI requests.',
        callNotifications: 'Call Notifications', clearResolved: 'Clear Resolved',
        noPendingHint: 'All calls have been handled'
      },
      anomaly: {
        pageTitle: 'Anomaly Detection', subtitle: 'Automated pattern analysis',
        spike: 'Spike', frequency: 'Frequency', severity: 'Severity',
        warning: 'Warning', critical: 'Critical', machine: 'Machine', score: 'Score',
        noAnomalies: 'No anomalies detected.',
        totalDetected: 'Total Detected',
        analyzing: 'Analyzing data…', analyzingDetail: 'Running anomaly detection algorithms…',
        noAnomaliesDetail: 'All systems are operating normally.'
      },
      states: {
        STOP: 'Stop', RUN: 'Run', RESPONSE: 'Response',
        ACCEPT: 'Accepted', WAIT: 'Waiting', REJECT: 'Rejected', FIX: 'Fix Complete'
      },
      roles: {
        Admin: 'Admin', Technical: 'Technician', Production: 'Production', DRI: 'DRI'
      },
      home: {
        welcome: 'Welcome',
        learnMore: 'Learn about building Web apps with ASP.NET Core.',
        privacyTitle: 'Privacy Policy',
        privacyBody: 'Use this page to detail your site\'s privacy policy.'
      },
      error: {
        title: 'Error.',
        subtitle: 'An error occurred while processing your request.',
        requestId: 'Request ID:',
        devModeTitle: 'Development Mode',
        devModeBody: 'Swapping to Development environment will display more detailed information about the error that occurred.',
        devModeWarning: 'The Development environment shouldn\'t be enabled for deployed applications. It can result in displaying sensitive information from exceptions to end users. For local debugging, enable the Development environment by setting the ASPNETCORE_ENVIRONMENT environment variable to Development and restarting the app.'
      }
    },
    
    /* ══════════════════════════ CHINESE ══════════════════════════ */
    cn: {
      common: {
        save: '保存', cancel: '取消', close: '关闭', confirm: '确认',
        delete: '删除', loading: '加载中…', search: '搜索',
        filter: '筛选', reset: '重置', export: '导出',
        noData: '未找到数据', all: '全部', from: '从', to: '至',
        status: '状态', date: '日期', location: '区域',
        operation: '工序', machine: '设备', employee: '员工',
        description: '描述', action: '操作', total: '合计',
        prev: '上一页', next: '下一页', page: '第', of: '共',
        yes: '是', no: '否', apply: '应用', clear: '清除',
        exportExcel: '📥 导出Excel', submit: '提交', skip: '跳过',
        markAllRead: '全部标记为已读', searchPlaceholder: '搜索…',
        unread: '未读', pageSize: '条/页', showing: '显示',
        hidden: '已隐藏', items: '条', confirmDelete: '确认删除',
        deleteFailed: '删除失败', error: '错误', connectionError: '连接错误',
        enableSoundPrompt: '启用通知声音', enableSound: '启用声音',
        today: '今天', handling: '处理中', lastUpdated: '最近更新',
        refresh: '刷新', errorLoad: '加载数据失败', retry: '重试',
        reconnecting: '重新连接中…', connected: '已连接', disconnected: '已断开'
      },
      login: {
        brand: 'SIGMA WORLDWIDE',
        systemLine1: '设备', systemLine2: '停机', systemLine3: '系统',
        tagline: '本系统用于管理生产过程中的设备停机时间。',
        statusProd: '生产实时追踪',
        statusTech: '技术响应管理',
        statusReport: '停机数据分析与报告',
        signIn: '登录',
        signInPrompt: '请输入您的账户信息',
        username: '用户名', usernamePH: '请输入用户名…',
        password: '密码', passwordPH: '请输入密码…',
        loginBtn: '登录 →',
        footer: 'Sigma Worldwide · 停机系统'
      },
      nav: {
        createDowntime: '录入停机',
        downtimeHistory: '停机记录',
        downtimeReport: '停机报告',
        notifDashboard: '通知看板',
        myNotifications: '我的通知',
        adminPanel: '管理后台',
        driDashboard: 'DRI 看板',
        logout: '退出',
        techResponses: '📬 技术团队响应',
        prodNotifs: '🛑 生产通知',
        allNotifs: '📋 全部通知',
        clearAll: '清除全部',
        noNotifs: '暂无通知。'
      },
      createDowntime: {
        pageTitle: '停机录入',
        subtitle: '记录新的停机事件',
        location: '区域', selectLocation: '-- 选择区域 --',
        operation: '工序', selectOperation: '-- 选择工序 --',
        machine: '设备/治具编号',
        searchMachine: '搜索设备…', selectMachine: '-- 选择设备/治具 --',
        employee: '员工', selectEmployee: '-- 选择员工 --',
        station: '站位', stationPH: '请输入站位…',
        category: '停机类别', selectCategory: '-- 选择类别 --',
        effect: '对生产的影响', selectEffect: '-- 选择影响 --',
        affectsProduction: '影响生产', noEffect: '无影响',
        description: '问题描述', descriptionPH: '描述问题…',
        image: '图片（可选）',
        callBtn: '🛑 呼叫', responseBtn: '⚙ 响应', runBtn: '▶ 恢复运行',
        rootCause: '根本原因', rootCausePH: '请输入根本原因…',
        actionTaken: '已采取的措施', actionTakenPH: '描述已采取的措施…',
        spareParts: '使用的备件', sparePartsPH: '列出使用的备件…',
        uploadImage: '上传图片', removeImage: '移除', scanBarcode: '扫描条码/二维码',
        confirmSave: '确认保存停机记录？'
      },
      notifDashboard: {
        pageTitle: '技术看板',
        subtitle: '实时停机通知',
        connecting: '连接中…', connected: '已连接', disconnected: '已断开',
        totalCalls: '总呼叫数', pending: '待处理', accepted: '已受理',
        rejected: '已拒绝', resolved: '已解决', waiting: '等待中',
        respond: '✅ 响应', reject: '❌ 拒绝', notEF: '🏢 非E&F',
        fixComplete: '🔧 响应 — 修复完成',
        rejectTitle: '❌ 拒绝维修请求',
        device: '设备', line: '工序',
        rejectPH: '请输入拒绝原因…（必填）',
        confirmReject: '❌ 确认拒绝',
        postRunTitle: '📋 填写维修详情',
        postRunSubtitle: '请在通知生产恢复前填写以下信息。',
        saveDetails: '💾 保存', skipDetails: '跳过',
        category: '停机类别', selectCategory: '-- 选择类别 --',
        effect: '影响', rootCause: '根本原因', rootCausePH: '请输入根本原因…',
        actionTaken: '已采取的措施', actionTakenPH: '描述已采取的措施…',
        spareParts: '使用的备件', sparePartsPH: '列出使用的备件…',
        postRunDesc: '问题描述', descPH: '问题描述…',
        selectTechnician: '-- 选择技术员 --',
        charCount: '字符'
      },
      prodNotifs: {
        pageTitle: '生产通知',
        subtitle: '来自技术团队的响应',
        totalNotifs: '总计', accepted: '已受理', waiting: '等待中', fixComplete: '修复完成',
        allTab: '全部', acceptTab: '✅ 已受理', waitTab: '⏳ 等待中', fixTab: '🔧 修复完成',
        noNotifs: '所选筛选条件下暂无通知。',
        callDRI: '📞 呼叫DRI', runMachine: '▶ 恢复运行',
        machine: '设备', line: '工序', employee: '员工',
        techResponse: '技术响应', fixedBy: '维修人', runningMsg: '已恢复运行'
      },
      downtimeList: {
        pageTitle: '停机记录', filters: '筛选条件',
        fromDate: '开始日期', toDate: '结束日期',
        operation: '工序', location: '区域', machine: '设备',
        station: '站位', employee: '员工', reason: '类别', effect: '影响',
        applyFilter: '🔍 筛选', resetFilter: '✕ 清除', exportExcel: '📥 导出Excel',
        noData: '未找到记录。', prevPage: '← 上一页', nextPage: '下一页 →',
        colDatetime: '时间', colOperation: '工序', colMachine: '设备/治具',
        colLocation: '区域', colState: '状态', colStation: '站位',
        colReason: '类别', colEffect: '影响', colDescription: '描述',
        colAction: '措施', colRootCause: '根本原因', colSpareParts: '备件',
        colEmployee: '员工', showRecords: '条/页',
        machineFixture: '设备名称', efNo: '设备编号',
        employee_name: '员工', downtime_start_time: '停机开始时间'
      },
      report: {
        pageTitle: '停机报告',
        apply: '🔍 应用', reset: '✕ 重置', exportExcel: '📥 导出Excel',
        top5Title: '🏆 停机最多的前5台设备',
        top5ExportBtn: '按时间段导出前5停机',
        byLine: '按工序', byMachine: '按设备',
        summaryLine: '工序汇总', summaryMachine: '设备汇总',
        downtimeCount: '停机次数', totalDowntime: '总停机时间', totalMinutes: '合计（分钟）',
        operation: '工序', location: '区域', machine: '设备',
        fromDate: '开始日期', toDate: '结束日期', effect: '影响',
        stopPeriod: '停机（本周期）', stopMonth: '停机（本月）',
        avgRepair: '响应时长', downtimeMin: '停机时长（分钟）', repairCount: '维修次数',
        mttr: 'MTTR — 平均维修时间（分钟）',
        recordsPerPage: '条/页', activeFilters: '已启用筛选',
        totalLines: '总工序数', totalDowntimeEvents: '总停机次数',
        affectedMachines: '受影响设备数',
        downtimeDistribution: '停机分布', downtimeByReason: '按类别划分的停机时间',
        filtercategory: '类别',
        reason: '类别', employeeName: '技术员姓名',
        dailyTrend: '每日停机趋势', responseTimeByTech: '技术员响应时间',
        detailByLine: '工序详情', machineDetail: '设备详情',
        detailRecords: '详细记录', searchPlaceholder: '搜索记录…',
        noResponseRecords: '未找到响应记录', noMachineData: '无设备数据',
        noRecordsMatch: '没有符合条件的记录', noDowntimeData: '无停机数据',
        noFaultCodeData: '暂无故障代码数据'
      },
      admin: {
        pageTitle: '管理后台', subtitle: '系统管理', badge: 'ADMIN',
        usersTab: '👤 用户管理', logsTab: '📋 通知日志', settingsTab: '⚙ 设置',
        addUser: '+ 添加用户', username: '用户名', password: '密码',
        role: '角色', selectRole: '-- 选择角色 --', saveUser: '保存用户', deleteUser: '删除',
        filterDate: '按日期筛选', filterType: '按类型筛选', noLogs: '未找到日志。',
        colDatetime: '时间', colType: '类型', colMessage: '消息',
        colMachine: '设备', colEmployee: '员工',
        downtimeTab: '停机记录', employeeTab: '员工管理', accountTab: '账户管理',
        filterOperation: '工序', filterState: '状态', filterFrom: '从', filterTo: '至',
        allStates: '全部状态', colId: 'ID', colState: '状态', colLocation: '区域',
        colReason: '原因', colEffect: '影响', colStation: '站位',
        colDescription: '描述', colAction: '措施',
        filterEmpId: '员工工号', addEmployee: '添加员工',
        colEmpId: '员工工号', colChineseName: '中文姓名', colEnglishName: '英文姓名',
        addAccount: '添加账户', resetPassword: '重置密码',
        accountLabel: '账户', newPassword: '新密码',
        generateBtn: '生成', generateTip: '密码将随机生成',
        confirmReset: '确认重置', confirmDeleteTitle: '确认删除',
        confirmDeleteMsg: '确定要删除此条目吗？',
        filterOperationPh: '按工序筛选…', filterEmpIdPh: '按员工工号搜索…',
        filterAccPh: '按用户名搜索…', passwordPh: '请输入密码…',
        newPasswordPh: '请输入新密码…', edit : '编辑', resetPwBtn: '重置密码'
        
      },
      dri: {
        pageTitle: 'DRI 看板', subtitle: '部门代表界面',
        accept: '接受', resolve: '标记为已解决', escalate: '升级处理',
        pending: '待处理', resolved: '已解决', noPending: '暂无待处理的DRI请求。',
        callNotifications: '呼叫通知', clearResolved: '清除已解决',
        noPendingHint: '所有呼叫已处理完毕'
      },
      anomaly: {
        pageTitle: '异常检测', subtitle: '自动化模式分析',
        spike: '峰值', frequency: '频率', severity: '严重性',
        warning: '警告', critical: '严重', machine: '设备', score: '分数',
        noAnomalies: '未检测到异常。',
        totalDetected: '检测总数',
        analyzing: '正在分析数据…', analyzingDetail: '运行异常检测算法中…',
        noAnomaliesDetail: '所有系统运行正常。'
      },
      states: {
        STOP: '停机', RUN: '运行', RESPONSE: '响应',
        ACCEPT: '已受理', WAIT: '等待中', REJECT: '已拒绝', FIX: '修复完成'
      },
      roles: {
        Admin: '管理员', Technical: '技术员', Production: '生产员', DRI: 'DRI'
      },
      home: {
        welcome: '欢迎',
        learnMore: '了解如何使用 ASP.NET Core 构建 Web 应用程序。',
        privacyTitle: '隐私政策',
        privacyBody: '请在此页面详细说明您的网站隐私政策。'
      },
      error: {
        title: '错误。',
        subtitle: '处理您的请求时发生错误。',
        requestId: '请求 ID：',
        devModeTitle: '开发模式',
        devModeBody: '切换到开发环境将显示有关所发生错误的更详细信息。',
        devModeWarning: '不应在已部署的应用程序中启用开发环境。这可能导致向最终用户显示异常中的敏感信息。如需本地调试，请将 ASPNETCORE_ENVIRONMENT 环境变量设置为 Development 并重启应用以启用开发环境。'
      }
    }
  };
  
  /* ─────────────────────────────────────────────────────────────
  ENGINE
  ───────────────────────────────────────────────────────────── */
  var lang = localStorage.getItem(LANG_KEY) || 'en';
  
  function t(key) {
    var parts = key.split('.');
    var v = dict[lang] || dict['en'];
    for (var i = 0; i < parts.length; i++) {
      if (v == null || typeof v !== 'object') return key;
      v = v[parts[i]];
    }
    return (v != null && typeof v === 'string') ? v : key;
  }
  
  function apply() {
    document.querySelectorAll('[data-i18n]').forEach(function (el) {
      var k = el.getAttribute('data-i18n');
      var v = t(k);
      if (v !== k) el.textContent = v;
    });
    document.querySelectorAll('[data-i18n-ph]').forEach(function (el) {
      var k = el.getAttribute('data-i18n-ph');
      var v = t(k);
      if (v !== k) el.placeholder = v;
    });
    document.querySelectorAll('[data-i18n-title]').forEach(function (el) {
      var k = el.getAttribute('data-i18n-title');
      var v = t(k);
      if (v !== k) el.title = v;
    });
    /* flag highlight */
    document.querySelectorAll('.lang-flag').forEach(function (img) {
      var active = img.dataset.lang === lang;
      img.style.outline      = active ? '2px solid #0d6efd' : 'none';
      img.style.outlineOffset = active ? '2px' : '0';
      img.style.opacity      = active ? '1' : '0.45';
      img.style.transform    = active ? 'scale(1.12)' : 'scale(1)';
    });
    document.documentElement.lang = lang === 'cn' ? 'zh' : 'en';
  }
  
  function setLang(l) {
    if (!dict[l]) return;
    lang = l;
    localStorage.setItem(LANG_KEY, l);
    apply();
  }
  
  /* Public API */
  global.i18nT      = t;
  global.changeLang = setLang;
  global.getLang    = function () { return lang; };
  
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', apply);
  } else {
    apply();
  }
  
})(window);
