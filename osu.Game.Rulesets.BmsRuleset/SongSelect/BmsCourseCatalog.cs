using System;
using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal sealed class BmsCourseCatalog
{
    internal IReadOnlyList<BmsCourseDefinition> Courses => courses;

    internal event Action? Changed;

    private readonly List<BmsCourseDefinition> courses = [];

    internal void Replace(IEnumerable<BmsCourseDefinition> newCourses)
    {
        courses.Clear();
        courses.AddRange(newCourses.OrderBy(course => course.TableName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(course => course.Name, StringComparer.OrdinalIgnoreCase));
        Changed?.Invoke();
    }

    internal void Clear() => Replace([]);
}
