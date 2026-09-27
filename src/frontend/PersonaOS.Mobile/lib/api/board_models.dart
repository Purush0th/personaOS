import '../date_utils.dart';

/// Board columns, as the server names them.
class BoardColumns {
  static const backlog = 'backlog';
  static const todo = 'todo';
  static const inProgress = 'in_progress';
  static const done = 'done';

  static const all = [backlog, todo, inProgress, done];

  /// What the board itself shows.
  static const board = [todo, inProgress, done];

  static String label(String column) => switch (column) {
        backlog => 'Backlog',
        todo => 'To do',
        inProgress => 'In progress',
        done => 'Done',
        _ => column,
      };
}

/// The server refuses an unacknowledged change to a running sprint with this code.
const scopeChangeCode = 'scope_change_unacknowledged';

/// The Fibonacci value-point scale.
const valuePoints = [1, 2, 3, 5, 8, 13, 21];

/// A task under a goal, as listed with the goal.
class GoalTaskSummary {
  GoalTaskSummary({
    required this.id,
    required this.key,
    required this.title,
    required this.points,
    required this.column,
    required this.sprintNumber,
  });

  factory GoalTaskSummary.fromJson(Map<String, dynamic> json) => GoalTaskSummary(
        id: json['id'] as int,
        key: json['key'] as String,
        title: json['title'] as String,
        points: json['points'] as int?,
        column: json['column'] as String,
        sprintNumber: json['sprintNumber'] as int?,
      );

  final int id;
  final String key;
  final String title;
  final int? points;
  final String column;
  final int? sprintNumber;
}

/// A goal is the epic of the sprint board: goals do not nest, and their tasks carry the work.
/// A child goal, as listed with its parent: a year's quarter or a quarter's month.
class GoalChildSummary {
  GoalChildSummary({
    required this.id,
    required this.key,
    required this.title,
    required this.periodType,
    required this.slot,
    required this.status,
    required this.effectiveProgress,
  });

  factory GoalChildSummary.fromJson(Map<String, dynamic> json) => GoalChildSummary(
        id: json['id'] as int,
        key: json['key'] as String,
        title: json['title'] as String,
        periodType: json['periodType'] as String,
        slot: json['slot'] as String? ?? '',
        status: json['status'] as String,
        effectiveProgress: json['effectiveProgress'] as int? ?? 0,
      );

  final int id;
  final String key;
  final String title;
  final String periodType;
  final String slot;
  final String status;
  final int effectiveProgress;
}

/// A goal: year, quarter or month, nested year > quarter > month or standalone. Only monthly
/// goals hold tasks. Its dates follow its calendar slot.
class Goal {
  Goal({
    required this.id,
    required this.key,
    required this.title,
    required this.periodType,
    this.slot = '',
    this.periodStart,
    this.periodEnd,
    this.parentId,
    this.parentKey,
    required this.status,
    required this.progress,
    required this.effectiveProgress,
    required this.taskCount,
    required this.doneTaskCount,
    this.childCount = 0,
    this.completedChildCount = 0,
    this.completeProblem,
    required this.tasks,
    this.children = const [],
  });

  factory Goal.fromJson(Map<String, dynamic> json) => Goal(
        id: json['id'] as int,
        key: json['key'] as String? ?? '',
        title: json['title'] as String,
        periodType: json['periodType'] as String,
        slot: json['slot'] as String? ?? '',
        periodStart: DateTime.tryParse(json['periodStart'] as String? ?? ''),
        periodEnd: DateTime.tryParse(json['periodEnd'] as String? ?? ''),
        parentId: json['parentId'] as int?,
        parentKey: json['parentKey'] as String?,
        status: json['status'] as String,
        progress: json['progress'] as int,
        effectiveProgress: json['effectiveProgress'] as int,
        taskCount: json['taskCount'] as int? ?? 0,
        doneTaskCount: json['doneTaskCount'] as int? ?? 0,
        childCount: json['childCount'] as int? ?? 0,
        completedChildCount: json['completedChildCount'] as int? ?? 0,
        completeProblem: json['completeProblem'] as String?,
        tasks: (json['tasks'] as List<dynamic>? ?? const [])
            .map((e) => GoalTaskSummary.fromJson(e as Map<String, dynamic>))
            .toList(),
        children: (json['children'] as List<dynamic>? ?? const [])
            .map((e) => GoalChildSummary.fromJson(e as Map<String, dynamic>))
            .toList(),
      );

  final int id;
  final String key;
  final String title;
  final String periodType; // year | quarter | month

  /// "2026", "Q4 2026" or "Oct 2026".
  final String slot;

  /// First and last day of the goal; the last is its due date.
  final DateTime? periodStart;
  final DateTime? periodEnd;

  /// The year a quarter sits under, or the quarter a month sits under; null when standalone.
  final int? parentId;
  final String? parentKey;

  final String status; // active | completed

  /// Manually tracked; used only while the goal has no tasks and no child goals.
  final int progress;

  /// Completed 100; else the average of its child goals; else done tasks over tasks; else manual.
  final int effectiveProgress;
  final int taskCount;
  final int doneTaskCount;
  final int childCount;
  final int completedChildCount;

  /// Why it cannot be completed yet (open tasks, active child goals), or null.
  final String? completeProblem;
  final List<GoalTaskSummary> tasks;
  final List<GoalChildSummary> children;

  /// Progress set by hand: an open goal with no tasks and no child goals.
  bool get setsProgressByHand => status == 'active' && taskCount == 0 && childCount == 0;
}

class BoardTask {
  BoardTask({
    required this.id,
    required this.key,
    required this.title,
    this.description,
    this.points,
    required this.column,
    this.sprintNumber,
    this.goalId,
    this.goalKey,
    this.goalTitle,
    this.priority = 'medium',
    this.sprintKey,
    this.addedMidSprint = false,
    this.carryOverCount = 0,
    this.commentCount = 0,
    this.attachmentCount = 0,
  });

  factory BoardTask.fromJson(Map<String, dynamic> json) => BoardTask(
        id: json['id'] as int,
        key: json['key'] as String,
        title: json['title'] as String,
        description: json['description'] as String?,
        points: json['points'] as int?,
        column: json['column'] as String,
        sprintNumber: json['sprintNumber'] as int?,
        goalId: json['goalId'] as int?,
        goalKey: json['goalKey'] as String?,
        goalTitle: json['goalTitle'] as String?,
        priority: json['priority'] as String? ?? 'medium',
        sprintKey: json['sprintKey'] as String?,
        addedMidSprint: json['addedMidSprint'] as bool? ?? false,
        carryOverCount: json['carryOverCount'] as int? ?? 0,
        commentCount: json['commentCount'] as int? ?? 0,
        attachmentCount: json['attachmentCount'] as int? ?? 0,
      );

  final int id;
  final String key;
  final String title;
  final String? description;
  final int? points;
  final String column;
  final int? sprintNumber;
  final int? goalId;
  final String? goalKey;
  final String? goalTitle;
  final String priority;
  final String? sprintKey;
  final bool addedMidSprint;
  final int carryOverCount;
  final int commentCount;
  final int attachmentCount;
}

class SprintInfo {
  SprintInfo({
    required this.id,
    required this.key,
    required this.number,
    this.name,
    required this.status,
    required this.startsAtUtc,
    required this.endsAtUtc,
    this.committedPoints,
    this.addedPoints = 0,
    this.removedPoints = 0,
    this.completedPoints = 0,
    this.carriedOverPoints,
    this.totalPoints = 0,
    this.unestimatedCount = 0,
    this.scopeLocked = false,
  });

  factory SprintInfo.fromJson(Map<String, dynamic> json) => SprintInfo(
        id: json['id'] as int,
        key: json['key'] as String? ?? 'SPRINT-${json['number']}',
        number: json['number'] as int,
        name: json['name'] as String?,
        status: json['status'] as String,
        startsAtUtc: parseServerUtc(json['startsAtUtc'] as String),
        endsAtUtc: parseServerUtc(json['endsAtUtc'] as String),
        committedPoints: json['committedPoints'] as int?,
        addedPoints: json['addedPoints'] as int? ?? 0,
        removedPoints: json['removedPoints'] as int? ?? 0,
        completedPoints: json['completedPoints'] as int? ?? 0,
        carriedOverPoints: json['carriedOverPoints'] as int?,
        totalPoints: json['totalPoints'] as int? ?? 0,
        unestimatedCount: json['unestimatedCount'] as int? ?? 0,
        scopeLocked: json['scopeLocked'] as bool? ?? false,
      );

  final int id;

  /// The user-facing key, e.g. SPRINT-2.
  final String key;
  final int number;

  /// Optional name the user gave the sprint.
  final String? name;
  final String status; // planned | active | closed
  final DateTime startsAtUtc;
  final DateTime endsAtUtc;
  final int? committedPoints;
  final int addedPoints;
  final int removedPoints;
  final int completedPoints;
  final int? carriedOverPoints;
  final int totalPoints;
  final int unestimatedCount;
  final bool scopeLocked;

  bool get isActive => status == 'active';
}

class BoardView {
  BoardView({
    required this.sprint,
    required this.velocity,
    required this.wipLimit,
    required this.columns,
  });

  factory BoardView.fromJson(Map<String, dynamic> json) {
    List<BoardTask> tasks(String name) => (json[name] as List<dynamic>? ?? const [])
        .map((e) => BoardTask.fromJson(e as Map<String, dynamic>))
        .toList();
    return BoardView(
      sprint: json['sprint'] == null
          ? null
          : SprintInfo.fromJson(json['sprint'] as Map<String, dynamic>),
      velocity: (json['velocity'] as num?)?.toDouble(),
      wipLimit: json['wipLimit'] as int? ?? 3,
      columns: {
        BoardColumns.todo: tasks('todo'),
        BoardColumns.inProgress: tasks('inProgress'),
        BoardColumns.done: tasks('done'),
      },
    );
  }

  /// Null when no sprint is running: the board is empty until one is started.
  final SprintInfo? sprint;
  final double? velocity;
  final int wipLimit;

  /// Tasks by column id, in board order.
  final Map<String, List<BoardTask>> columns;

  /// The board is the running sprint only; the backlog lives on its own page in the web app.
  List<String> get visibleColumns => BoardColumns.board;
}

class SprintReport {
  SprintReport({required this.velocity, required this.sprints});

  factory SprintReport.fromJson(Map<String, dynamic> json) => SprintReport(
        velocity: (json['velocity'] as num?)?.toDouble(),
        sprints: (json['sprints'] as List<dynamic>)
            .map((e) => SprintInfo.fromJson(e as Map<String, dynamic>))
            .toList(),
      );

  final double? velocity;
  final List<SprintInfo> sprints;
}

/// One sprint on the plan, with the work sitting in it.
class SprintPlan {
  SprintPlan({required this.sprint, required this.tasks});

  factory SprintPlan.fromJson(Map<String, dynamic> json) => SprintPlan(
        sprint: SprintInfo.fromJson(json['sprint'] as Map<String, dynamic>),
        tasks: (json['tasks'] as List<dynamic>? ?? const [])
            .map((e) => BoardTask.fromJson(e as Map<String, dynamic>))
            .toList(),
      );

  final SprintInfo sprint;
  final List<BoardTask> tasks;
}

/// The plan: the running sprint, the sprints planned after it, and the backlog.
class PlanView {
  PlanView({required this.sprints, required this.backlog});

  factory PlanView.fromJson(Map<String, dynamic> json) => PlanView(
        sprints: (json['sprints'] as List<dynamic>? ?? const [])
            .map((e) => SprintPlan.fromJson(e as Map<String, dynamic>))
            .toList(),
        backlog: (json['backlog'] as List<dynamic>? ?? const [])
            .map((e) => BoardTask.fromJson(e as Map<String, dynamic>))
            .toList(),
      );

  final List<SprintPlan> sprints;
  final List<BoardTask> backlog;
}
