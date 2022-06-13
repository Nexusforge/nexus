# Python <= 3.9
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from enum import IntEnum
import json
from typing import Any, Optional, Type
from uuid import UUID

import pytest
from nexus_api import (JsonEncoder, JsonEncoderOptions, to_camel_case,
                       to_snake_case)

class TestEnum(IntEnum):
    A = 1
    B = 2

@dataclass(frozen=True)
class TestClass:
    int_value: int
    float_value: float
    str_value: str
    datetime_value: datetime
    timedelta_value: timedelta
    enum_value: TestEnum
    nested_value: Optional[TestClassNested] = None

@dataclass(frozen=True)
class TestClassNested:
    uuid_value: UUID

def can_encode_dataclass_test():

    # arrange
    options = JsonEncoderOptions(property_name_encoder = to_camel_case)
    
    value = TestClass(
        int_value=1, 
        float_value=1.1, 
        str_value="1", 
        datetime_value=datetime(2020, 1, 2, 3, 4, 5, microsecond=678901, tzinfo = timezone.utc),
        timedelta_value=timedelta(days=1,hours=2,minutes=3,seconds=4,milliseconds=5,microseconds=6),
        enum_value=TestEnum.A,
        nested_value=TestClassNested(uuid_value=UUID("9842d4b6-0b89-4b56-ad0f-acb5b8be8112")))
    
    expected = \
"""
{
    "intValue": 1,
    "floatValue": 1.1,
    "strValue": "1",
    "datetimeValue": "2020-01-02T03:04:05.678901Z",
    "timedeltaValue": "1.02:03:04.005006",
    "enumValue": "A",
    "nestedValue": {
        "uuidValue": "9842d4b6-0b89-4b56-ad0f-acb5b8be8112"
    }
}
"""

    # act
    encoded = JsonEncoder.encode(value, options)
    actual = json.dumps(encoded, indent=4)

    # assert
    assert expected.strip() == actual

@pytest.mark.parametrize(
    "value, expected", 
    [
        (1, "1"),
        (1.1, "1.1"),
        ("1", '"1"'),
        (True, "true"),
        (False, "false"),
        (TestEnum.A, '"A"'),
    ])
def can_encode_primitive_test(value: Any, expected: str):

    # arrange
    
    # act
    encoded = JsonEncoder.encode(value)
    actual = json.dumps(encoded)

    # assert
    assert expected == actual

def can_decode_dataclass_test():

    # arrange
    options = JsonEncoderOptions(property_name_decoder = to_snake_case)
    
    json_string = \
"""
{
    "intValue": 1,
    "floatValue": 1.1,
    "strValue": "1",
    "datetimeValue": "2020-01-02T03:04:05.678901Z",
    "timedeltaValue": "1.02:03:04.005006",
    "enumValue": "A",
    "nestedValue": {
        "uuidValue": "9842d4b6-0b89-4b56-ad0f-acb5b8be8112"
    }
}
"""
    expected = TestClass(
        int_value=1, 
        float_value=1.1, 
        str_value="1", 
        datetime_value=datetime(2020, 1, 2, 3, 4, 5, microsecond=678901, tzinfo=timezone.utc),
        timedelta_value=timedelta(days=1,hours=2,minutes=3,seconds=4,milliseconds=5,microseconds=6),
        enum_value=TestEnum.A,
        nested_value=TestClassNested(uuid_value=UUID("9842d4b6-0b89-4b56-ad0f-acb5b8be8112")))

    # act

    decoded = json.loads(json_string)
    actual = JsonEncoder.decode(TestClass, decoded, options)

    # assert
    assert expected == actual

    
@pytest.mark.parametrize(
    "json_string, cls, expected", 
    [
        ("1", int, 1),
        ("1.1", float, 1.1),
        ('"1"', str, "1"),
        ("true", bool, True),
        ("false", bool, False),
        ('"A"', TestEnum, TestEnum.A)
    ])
def can_decode_primitive_test(json_string: Any, cls: Type, expected: str):

    # arrange
    
    # act
    decoded = json.loads(json_string)
    actual = JsonEncoder.decode(cls, decoded)

    # assert
    assert expected == actual